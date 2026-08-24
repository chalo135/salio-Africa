from fastapi import FastAPI

app = FastAPI(title="Salio API")


@app.get("/api/v1/health")
def health():
    return {"ok": True}
