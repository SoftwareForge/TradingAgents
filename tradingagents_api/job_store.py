from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime, timezone
from threading import Lock
from typing import Any, Optional
from uuid import uuid4

from .schemas import JobStatus


def utcnow() -> datetime:
    return datetime.now(timezone.utc)


@dataclass
class JobRecord:
    job_id: str
    status: JobStatus
    ticker: str
    analysis_date: str
    provider: str
    payload: dict[str, Any]
    progress: int = 0
    current_stage: str = "queued"
    created_at: datetime = field(default_factory=utcnow)
    started_at: Optional[datetime] = None
    finished_at: Optional[datetime] = None
    cancel_requested: bool = False
    last_error: Optional[str] = None
    result: Optional[dict[str, Any]] = None
    events: list[dict[str, Any]] = field(default_factory=list)


class JobStore:
    def __init__(self) -> None:
        self._jobs: dict[str, JobRecord] = {}
        self._lock = Lock()

    def create(self, payload: dict[str, Any]) -> JobRecord:
        with self._lock:
            job_id = str(uuid4())
            job = JobRecord(
                job_id=job_id,
                status="queued",
                ticker=payload["ticker"],
                analysis_date=payload["analysis_date"],
                provider=payload["provider"],
                payload=payload,
            )
            job.events.append(
                {"ts": utcnow().isoformat(), "type": "status", "message": "queued"}
            )
            self._jobs[job_id] = job
            return job

    def get(self, job_id: str) -> Optional[JobRecord]:
        with self._lock:
            return self._jobs.get(job_id)

    def request_cancel(self, job_id: str) -> Optional[JobRecord]:
        with self._lock:
            job = self._jobs.get(job_id)
            if not job:
                return None
            if job.status in ("succeeded", "failed", "canceled"):
                return job
            job.cancel_requested = True
            if job.status == "queued":
                job.status = "canceled"
                job.progress = 100
                job.current_stage = "canceled"
                job.finished_at = utcnow()
            else:
                job.status = "cancel_requested"
                job.current_stage = "cancel_requested"
            job.events.append(
                {
                    "ts": utcnow().isoformat(),
                    "type": "status",
                    "message": job.current_stage,
                }
            )
            return job

    def mark_running(self, job_id: str, stage: str) -> None:
        with self._lock:
            job = self._jobs[job_id]
            if job.cancel_requested and job.status == "canceled":
                return
            job.status = "running"
            job.started_at = utcnow()
            job.progress = 10
            job.current_stage = stage
            job.events.append(
                {"ts": utcnow().isoformat(), "type": "status", "message": stage}
            )

    def mark_success(self, job_id: str, result: dict[str, Any]) -> None:
        with self._lock:
            job = self._jobs[job_id]
            if job.cancel_requested and job.status == "cancel_requested":
                job.status = "canceled"
                job.current_stage = "canceled"
            else:
                job.status = "succeeded"
                job.current_stage = "completed"
            job.progress = 100
            job.result = result
            job.finished_at = utcnow()
            job.events.append(
                {
                    "ts": utcnow().isoformat(),
                    "type": "status",
                    "message": job.current_stage,
                }
            )

    def mark_failed(self, job_id: str, error: str) -> None:
        with self._lock:
            job = self._jobs[job_id]
            job.status = "failed"
            job.progress = 100
            job.current_stage = "failed"
            job.last_error = error
            job.finished_at = utcnow()
            job.events.append(
                {"ts": utcnow().isoformat(), "type": "error", "message": error}
            )

    def mark_canceled(self, job_id: str, reason: str = "Canceled by user") -> None:
        with self._lock:
            job = self._jobs[job_id]
            job.status = "canceled"
            job.progress = 100
            job.current_stage = "canceled"
            job.finished_at = utcnow()
            job.last_error = None
            job.events.append(
                {"ts": utcnow().isoformat(), "type": "status", "message": reason}
            )

    def add_event(self, job_id: str, event: dict[str, Any]) -> None:
        with self._lock:
            job = self._jobs[job_id]
            enriched = {"ts": utcnow().isoformat(), **event}
            job.events.append(enriched)
            progress = event.get("progress")
            if isinstance(progress, int):
                job.progress = max(0, min(100, progress))
            stage = event.get("stage")
            if isinstance(stage, str) and stage:
                job.current_stage = stage
            if len(job.events) > 5000:
                job.events = job.events[-5000:]

    def list_jobs(self) -> list[JobRecord]:
        with self._lock:
            return list(self._jobs.values())
