from __future__ import annotations

import os
import threading
import time
from concurrent.futures import ThreadPoolExecutor

from fastapi import FastAPI, HTTPException
from fastapi.responses import JSONResponse

from .job_store import JobStore
from .runner import AnalysisCancelled, run_analysis_job
from .schemas import (
    AnalysisCreateRequest,
    AnalysisCreateResponse,
    AnalysisResultResponse,
    AnalysisStatusResponse,
)


app = FastAPI(title="TradingAgents API", version="0.1.0")
job_store = JobStore()
executor = ThreadPoolExecutor(max_workers=int(os.getenv("TRADINGAGENTS_API_WORKERS", "1")))


def _run_job(job_id: str) -> None:
    job = job_store.get(job_id)
    if job is None:
        return
    if job.status == "canceled":
        return
    stop_heartbeat = threading.Event()

    def _should_cancel() -> bool:
        current = job_store.get(job_id)
        return bool(current and current.cancel_requested)

    def _heartbeat() -> None:
        tick = 0
        while not stop_heartbeat.wait(2.0):
            tick += 1
            current = job_store.get(job_id)
            if current is None:
                return
            if current.status in ("succeeded", "failed", "canceled"):
                return
            job_store.add_event(
                job_id,
                {
                    "type": "heartbeat",
                    "stage": current.current_stage or "running_analysis",
                    "phase": "execution",
                    "team": "System",
                    "agent": "JobRunner",
                    "progress": current.progress,
                    "message": f"Analysis running... ({tick * 2}s)",
                },
            )

    heartbeat_thread = threading.Thread(target=_heartbeat, daemon=True)
    heartbeat_thread.start()

    try:
        job_store.mark_running(job_id, "running_analysis")
        result = run_analysis_job(
            job.payload,
            on_event=lambda ev: job_store.add_event(job_id, ev),
            should_cancel=_should_cancel,
        )
        job_store.mark_success(job_id, result)
    except AnalysisCancelled:
        job_store.mark_canceled(job_id, "Canceled by user")
    except Exception as exc:
        job_store.mark_failed(job_id, str(exc))
    finally:
        stop_heartbeat.set()


@app.get("/health")
def health() -> dict[str, str]:
    return {"status": "ok"}


@app.post("/v1/analyses", response_model=AnalysisCreateResponse, status_code=202)
def create_analysis(req: AnalysisCreateRequest) -> AnalysisCreateResponse:
    payload = req.model_dump()
    payload["provider"] = payload["provider"].lower()
    job = job_store.create(payload)
    executor.submit(_run_job, job.job_id)
    return AnalysisCreateResponse(job_id=job.job_id, status=job.status)


@app.get("/v1/analyses/{job_id}", response_model=AnalysisStatusResponse)
def get_analysis_status(job_id: str) -> AnalysisStatusResponse:
    job = job_store.get(job_id)
    if job is None:
        raise HTTPException(status_code=404, detail="Job not found")

    return AnalysisStatusResponse(
        job_id=job.job_id,
        status=job.status,
        progress=job.progress,
        current_stage=job.current_stage,
        ticker=job.ticker,
        analysis_date=job.analysis_date,
        provider=job.provider,
        created_at=job.created_at,
        started_at=job.started_at,
        finished_at=job.finished_at,
        last_error=job.last_error,
        cancel_requested=job.cancel_requested,
    )


@app.get("/v1/analyses")
def list_analyses(status: str | None = None, limit: int = 20) -> JSONResponse:
    jobs = job_store.list_jobs()
    jobs.sort(key=lambda j: j.created_at, reverse=True)

    if status:
        status_lower = status.lower()
        jobs = [j for j in jobs if j.status == status_lower]

    jobs = jobs[: max(1, min(limit, 200))]
    payload = [
        {
            "job_id": j.job_id,
            "status": j.status,
            "progress": j.progress,
            "current_stage": j.current_stage,
            "ticker": j.ticker,
            "analysis_date": j.analysis_date,
            "provider": j.provider,
            "created_at": j.created_at.isoformat(),
            "started_at": j.started_at.isoformat() if j.started_at else None,
            "finished_at": j.finished_at.isoformat() if j.finished_at else None,
            "cancel_requested": j.cancel_requested,
        }
        for j in jobs
    ]
    return JSONResponse({"jobs": payload})


@app.get("/v1/analyses/{job_id}/result", response_model=AnalysisResultResponse)
def get_analysis_result(job_id: str) -> AnalysisResultResponse:
    job = job_store.get(job_id)
    if job is None:
        raise HTTPException(status_code=404, detail="Job not found")
    if job.status in ("queued", "running", "cancel_requested"):
        raise HTTPException(status_code=409, detail="Job not completed yet")
    if job.status == "canceled":
        raise HTTPException(status_code=409, detail="Job was canceled")
    if job.status == "failed":
        raise HTTPException(status_code=500, detail=job.last_error or "Job failed")
    if not job.result:
        raise HTTPException(status_code=500, detail="Job completed without result")

    return AnalysisResultResponse(
        job_id=job.job_id,
        status=job.status,
        rating=job.result["rating"],
        final_trade_decision=job.result["final_trade_decision"],
        report_sections=job.result["report_sections"],
        meta=job.result["meta"],
    )


@app.get("/v1/analyses/{job_id}/events")
def get_analysis_events(job_id: str) -> JSONResponse:
    job = job_store.get(job_id)
    if job is None:
        raise HTTPException(status_code=404, detail="Job not found")
    return JSONResponse({"job_id": job_id, "events": job.events})


@app.post("/v1/analyses/{job_id}/cancel")
def cancel_analysis(job_id: str) -> dict[str, str]:
    job = job_store.request_cancel(job_id)
    if job is None:
        raise HTTPException(status_code=404, detail="Job not found")
    return {"job_id": job_id, "status": job.status}


def run() -> None:
    import uvicorn

    host = os.getenv("TRADINGAGENTS_API_HOST", "127.0.0.1")
    port = int(os.getenv("TRADINGAGENTS_API_PORT", "8081"))
    uvicorn.run("tradingagents_api.main:app", host=host, port=port, reload=False)


if __name__ == "__main__":
    run()
