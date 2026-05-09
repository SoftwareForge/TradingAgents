from langchain_core.messages import HumanMessage, RemoveMessage

# Import tools from separate utility files
from tradingagents.agents.utils.core_stock_tools import (
    get_stock_data
)
from tradingagents.agents.utils.technical_indicators_tools import (
    get_indicators
)
from tradingagents.agents.utils.fundamental_data_tools import (
    get_fundamentals,
    get_balance_sheet,
    get_cashflow,
    get_income_statement
)
from tradingagents.agents.utils.news_data_tools import (
    get_news,
    get_insider_transactions,
    get_global_news
)


def get_language_instruction() -> str:
    """Return a prompt instruction for the configured output language.

    Returns empty string when English (default), so no extra tokens are used.
    Only applied to user-facing agents (analysts, portfolio manager).
    Internal debate agents stay in English for reasoning quality.
    """
    from tradingagents.dataflows.config import get_config
    lang = get_config().get("output_language", "English")
    if lang.strip().lower() == "english":
        return ""
    return f" Write your entire response in {lang}."


def get_report_verbosity() -> str:
    """Return normalized report verbosity profile."""
    from tradingagents.dataflows.config import get_config
    value = str(get_config().get("report_verbosity", "standard")).strip().lower()
    if value == "compact":
        return "compact"
    if value == "original":
        return "original"
    return "standard"


def get_analyst_report_instruction() -> str:
    """Return a strict, parser-friendly report structure for analyst outputs."""
    verbosity = get_report_verbosity()
    if verbosity == "original":
        # Keep legacy/original TradingAgents analyst prompts unchanged.
        return ""
    if verbosity == "compact":
        return (
            " Use this exact markdown structure and headings in this order:\n"
            "1) **Executive Summary**: 3-5 sentences (max 180 words)\n"
            "2) **Key Evidence**: 4-6 bullet points (no fluff)\n"
            "3) **Actionable Implications**: 3-5 bullet points\n"
            "4) **Key Data Table**: markdown table with max 6 rows.\n"
            "Keep evidence dense, avoid repetition, and prefer concrete numbers."
        )
    return (
        " Use this exact markdown structure and headings in this order:\n"
        "1) **Executive Summary**: 5-10 sentences (max 300 words)\n"
        "2) **Key Evidence**: 5-10 bullet points\n"
        "3) **Actionable Implications**: 5-10 bullet points\n"
        "4) **Key Data Table**: markdown table with max 10 rows.\n"
        "Use clear evidence and avoid redundant points."
    )


def get_debate_turn_instruction() -> str:
    """Return instruction that enforces concise, contrastive debate turns."""
    verbosity = get_report_verbosity()
    if verbosity == "original":
        # Keep legacy/original TradingAgents debate prompts unchanged.
        return ""
    if verbosity == "compact":
        return (
            " Turn limit: 120-180 words. Include only new or contrasting arguments, "
            "explicitly avoid repeating earlier points unless needed for rebuttal."
        )
    return (
        " Turn limit: 150-200 words. Include only new or contrasting arguments, "
        "explicitly avoid repeating earlier points unless needed for rebuttal."
    )


def build_instrument_context(ticker: str) -> str:
    """Describe the exact instrument so agents preserve exchange-qualified tickers."""
    return (
        f"The instrument to analyze is `{ticker}`. "
        "Use this exact ticker in every tool call, report, and recommendation, "
        "preserving any exchange suffix (e.g. `.TO`, `.L`, `.HK`, `.T`)."
    )

def create_msg_delete():
    def delete_messages(state):
        """Clear messages and add placeholder for Anthropic compatibility"""
        messages = state["messages"]

        # Remove all messages
        removal_operations = [RemoveMessage(id=m.id) for m in messages]

        # Add a minimal placeholder message
        placeholder = HumanMessage(content="Continue")

        return {"messages": removal_operations + [placeholder]}

    return delete_messages


        
