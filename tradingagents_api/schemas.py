from __future__ import annotations

from datetime import datetime
from typing import Any, Literal, Optional

from pydantic import BaseModel, Field


JobStatus = Literal[
    "queued",
    "running",
    "succeeded",
    "failed",
    "cancel_requested",
    "canceled",
]


class AnalysisCreateRequest(BaseModel):
    ticker: str = Field(min_length=1, description="Ticker symbol, e.g. NVDA")
    analysis_date: str = Field(description="Analysis date in YYYY-MM-DD")
    provider: str = Field(default="openai", description="LLM provider key")
    deep_model: str = Field(min_length=1)
    quick_model: str = Field(min_length=1)
    research_depth: int = Field(default=1, ge=1, le=5)
    language: str = Field(default="English")
    report_verbosity: Literal["standard", "compact"] = "standard"
    backend_url: Optional[str] = None
    analysts: Optional[list[str]] = None
    checkpoint_enabled: bool = False


class AnalysisCreateResponse(BaseModel):
    job_id: str
    status: JobStatus


class AnalysisStatusResponse(BaseModel):
    job_id: str
    status: JobStatus
    progress: int = Field(ge=0, le=100)
    current_stage: str
    ticker: str
    analysis_date: str
    provider: str
    created_at: datetime
    started_at: Optional[datetime] = None
    finished_at: Optional[datetime] = None
    last_error: Optional[str] = None
    cancel_requested: bool = False


class AnalysisResultResponse(BaseModel):
    job_id: str
    status: JobStatus
    rating: str
    final_trade_decision: str
    report_sections: dict[str, Any]
    meta: dict[str, Any]
