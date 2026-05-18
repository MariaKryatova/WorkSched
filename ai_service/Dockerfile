FROM python:3.9-slim

WORKDIR /app

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY train_model.py .
COPY ai_service.py .
COPY models/ ./models/

EXPOSE 8000

CMD ["python", "ai_service.py"]