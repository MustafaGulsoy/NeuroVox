FROM python:3.12-slim

WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg gcc libc6-dev curl && rm -rf /var/lib/apt/lists/* \
    && useradd --create-home --uid 10001 neurovox

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY app.py turkish_nlp.py stats.py ./

ENV NEUROVOX_STT_MODEL=small \
    NEUROVOX_AUDIO_ROOT=/data/audio \
    NEUROVOX_MODEL_DIR=/models \
    HF_HOME=/cache/hf
RUN mkdir -p /data/audio /models /cache/hf && chown -R neurovox /cache /models
USER neurovox
EXPOSE 8000

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s CMD curl -fs http://localhost:8000/health || exit 1
# One worker: the Whisper model is large and analysis is serialised by the API anyway.
CMD ["uvicorn", "app:app", "--host", "0.0.0.0", "--port", "8000", "--workers", "1"]
