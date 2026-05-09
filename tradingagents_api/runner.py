from __future__ import annotations

from copy import deepcopy
from typing import Any, Callable, Optional

from tradingagents.default_config import DEFAULT_CONFIG
from tradingagents.graph.trading_graph import TradingAgentsGraph


DEFAULT_ANALYSTS = ["market", "social", "news", "fundamentals"]
SECTION_META: dict[str, tuple[str, str, int]] = {
    "market_report": ("Analyst Team", "analysts", 20),
    "sentiment_report": ("Analyst Team", "analysts", 30),
    "news_report": ("Analyst Team", "analysts", 40),
    "fundamentals_report": ("Analyst Team", "analysts", 50),
    "investment_plan": ("Research Team", "research", 65),
    "trader_investment_plan": ("Trading Team", "trading", 80),
    "final_trade_decision": ("Risk & Portfolio", "risk_portfolio", 95),
}

class AnalysisCancelled(Exception):
    """Raised when a running analysis is canceled by user request."""


def _build_config(payload: dict[str, Any]) -> dict[str, Any]:
    config = deepcopy(DEFAULT_CONFIG)
    config["llm_provider"] = payload["provider"].lower()
    config["deep_think_llm"] = payload["deep_model"]
    config["quick_think_llm"] = payload["quick_model"]
    config["max_debate_rounds"] = payload["research_depth"]
    config["max_risk_discuss_rounds"] = payload["research_depth"]
    config["output_language"] = payload["language"]
    config["checkpoint_enabled"] = payload["checkpoint_enabled"]
    config["backend_url"] = payload.get("backend_url")
    return config


def _extract_message_text(message: Any) -> str | None:
    content = getattr(message, "content", None)
    if content is None:
        return None
    if isinstance(content, str):
        text = content.strip()
        return text or None
    if isinstance(content, dict):
        text = str(content.get("text", "")).strip()
        return text or None
    if isinstance(content, list):
        parts: list[str] = []
        for item in content:
            if isinstance(item, str):
                parts.append(item.strip())
            elif isinstance(item, dict):
                if item.get("type") == "text":
                    parts.append(str(item.get("text", "")).strip())
        text = " ".join(p for p in parts if p)
        return text or None
    text = str(content).strip()
    return text or None


def _detect_event_identity(message: Any) -> tuple[str, str]:
    msg_cls = type(message).__name__.lower()
    if "tool" in msg_cls:
        return ("Tooling", "Tool")
    if "human" in msg_cls:
        return ("Control", "User/System")
    if "ai" in msg_cls:
        return ("Agents", "LLM Agent")
    return ("System", "System")


def _emit(on_event: Optional[Callable[[dict[str, Any]], None]], payload: dict[str, Any]) -> None:
    if on_event:
        on_event(payload)


def run_analysis_job(
    payload: dict[str, Any],
    on_event: Optional[Callable[[dict[str, Any]], None]] = None,
    should_cancel: Optional[Callable[[], bool]] = None,
) -> dict[str, Any]:
    config = _build_config(payload)
    selected_analysts = payload.get("analysts") or DEFAULT_ANALYSTS
    graph = TradingAgentsGraph(
        selected_analysts=selected_analysts,
        config=config,
        debug=False,
    )
    ticker = payload["ticker"]
    analysis_date = payload["analysis_date"]

    graph.ticker = ticker
    graph._resolve_pending_entries(ticker)
    past_context = graph.memory_log.get_past_context(ticker)
    init_agent_state = graph.propagator.create_initial_state(
        ticker, analysis_date, past_context=past_context
    )
    args = graph.propagator.get_graph_args()

    trace: list[dict[str, Any]] = []
    _emit(
        on_event,
        {
            "type": "stage",
            "stage": "starting",
            "phase": "boot",
            "team": "System",
            "agent": "TradingAgentsGraph",
            "progress": 5,
            "message": f"Starting analysis for {ticker} on {analysis_date}",
        },
    )

    chunk_index = 0
    for chunk in graph.graph.stream(init_agent_state, **args):
        if should_cancel and should_cancel():
            _emit(
                on_event,
                {
                    "type": "stage",
                    "stage": "canceled",
                    "phase": "canceled",
                    "team": "System",
                    "agent": "TradingAgentsGraph",
                    "progress": 100,
                    "message": "Cancellation requested. Stopping analysis.",
                },
            )
            raise AnalysisCancelled("Cancellation requested by user")

        chunk_index += 1
        trace.append(chunk)
        _emit(
            on_event,
            {
                "type": "chunk",
                "stage": "running_analysis",
                "phase": "execution",
                "team": "System",
                "agent": "LangGraph",
                "message": f"Received chunk #{chunk_index}",
            },
        )
        for message in chunk.get("messages", []):
            text = _extract_message_text(message)
            if text:
                team, agent = _detect_event_identity(message)
                _emit(
                    on_event,
                    {
                        "type": "message",
                        "stage": "running_analysis",
                        "phase": "execution",
                        "team": team,
                        "agent": agent,
                        "message": text,
                    },
                )

        for key, (team, phase, progress) in SECTION_META.items():
            value = chunk.get(key)
            if value:
                _emit(
                    on_event,
                    {
                        "type": "section",
                        "section": key,
                        "stage": "running_analysis",
                        "phase": phase,
                        "team": team,
                        "agent": "SectionWriter",
                        "progress": progress,
                        "message": f"Updated {key}",
                    },
                )

    final_state = trace[-1]
    graph.curr_state = final_state
    graph._log_state(analysis_date, final_state)
    graph.memory_log.store_decision(
        ticker=ticker,
        trade_date=analysis_date,
        final_trade_decision=final_state["final_trade_decision"],
    )
    rating = graph.process_signal(final_state["final_trade_decision"])

    _emit(
        on_event,
        {
            "type": "stage",
            "stage": "completed",
            "phase": "completed",
            "team": "System",
            "agent": "TradingAgentsGraph",
            "progress": 100,
            "message": "Analysis completed",
        },
    )

    report_sections = {
        "market_report": final_state.get("market_report"),
        "sentiment_report": final_state.get("sentiment_report"),
        "news_report": final_state.get("news_report"),
        "fundamentals_report": final_state.get("fundamentals_report"),
        "investment_plan": final_state.get("investment_plan"),
        "trader_investment_plan": final_state.get("trader_investment_plan"),
        "final_trade_decision": final_state.get("final_trade_decision"),
    }

    return {
        "rating": rating,
        "final_trade_decision": final_state["final_trade_decision"],
        "report_sections": report_sections,
        "meta": {
            "provider": payload["provider"],
            "deep_model": payload["deep_model"],
            "quick_model": payload["quick_model"],
            "research_depth": payload["research_depth"],
            "language": payload["language"],
        },
    }
