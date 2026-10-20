from fastapi.testclient import TestClient

from app.main import app


def test_health_reports_ok_and_no_model_configured() -> None:
    response = TestClient(app).get("/health")
    assert response.status_code == 200
    body = response.json()
    assert body["ok"] is True
    assert body["modelConfigured"] is False
    assert isinstance(body["provider"], str)
