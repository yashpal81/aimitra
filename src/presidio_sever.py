from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
from presidio_analyzer import AnalyzerEngine
import uvicorn

app = FastAPI()
analyzer = AnalyzerEngine()

class ScanRequest(BaseModel):
    text: str

@app.post("/scan")
def scan_text(request: ScanRequest):
    results = analyzer.analyze(text=request.text, entities=['PERSON', 'EMAIL_ADDRESS'], language='en')
    # Return serializable JSON metadata back to C#
    return [{"entity": r.entity_type, "start": r.start, "end": r.end} for r in results]

if __name__ == "__main__":
    uvicorn.run(app, host="127.0.0.1", port=8000)
    
