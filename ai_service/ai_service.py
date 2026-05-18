from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field
from typing import List
import pandas as pd
import numpy as np
import joblib
import os
from datetime import datetime
import uvicorn
import pyodbc

app = FastAPI(title="Leave Prediction Service", version="1.0")

def get_connection():
    conn_str = (
        r'DRIVER={SQL Server};'
        r'SERVER=DIABWIX\SQLEXPRESS;'
        r'DATABASE=WorkSched;'
        r'Trusted_Connection=yes;'
    )
    return pyodbc.connect(conn_str)

class ModelManager:
    def __init__(self):
        self.model = None
        self.scaler = None
        self.feature_names = None
        self.metadata = None
        self.load_models()
    
    def load_models(self):
        try:
            if os.path.exists('models/linear_regression_model.pkl'):
                self.model = joblib.load('models/linear_regression_model.pkl')
                self.scaler = joblib.load('models/scaler.pkl')
                self.feature_names = joblib.load('models/feature_names.pkl')
                self.metadata = joblib.load('models/metadata.pkl')
                print(f"Модель загружена (обучена: {self.metadata['train_date']})")
                print(f"   R² = {self.metadata['r2']:.3f}, MAE = {self.metadata['mae']:.2f}")
            else:
                print("Модель не найдена! Запустите train_model.py")
                self.create_fallback_model()
        except Exception as e:
            print(f"Ошибка загрузки: {e}")
            self.create_fallback_model()
    
    def create_fallback_model(self):
        class FallbackModel:
            def predict(self, X):
                return X[:, 0] * 0.8 + 3
        
        self.model = FallbackModel()
        self.scaler = None
        self.feature_names = ['PrevTotalDays', 'PrevVacationDays', 
                              'PrevSickDays', 'YearsWorked', 'DepartmentCode']
        print("Используется упрощенная модель-заглушка")

model_manager = ModelManager()

def get_employee_history(employee_id: int, year: int):
    conn = get_connection()
    
    prev_year = year - 1
    
    query = f"""
        SELECT 
            Type,
            SUM(DATEDIFF(day, StartDate, EndDate) + 1) as DaysCount
        FROM Leaves
        WHERE EmployeeId = {employee_id}
        AND Status = 'Approved'
        AND YEAR(StartDate) = {prev_year}
        AND DATEDIFF(day, StartDate, EndDate) <= 30
        GROUP BY Type
    """
    
    try:
        df = pd.read_sql(query, conn)
        
        vacation_days = 0
        sick_days = 0
        
        for _, row in df.iterrows():
            if row['Type'] == 'Vacation':
                vacation_days = row['DaysCount']
            elif row['Type'] == 'Sick':
                sick_days = row['DaysCount']
        
        total_days = vacation_days + sick_days
        
        if total_days == 0:
            any_year_query = f"""
                SELECT 
                    YEAR(StartDate) as Year,
                    Type,
                    SUM(DATEDIFF(day, StartDate, EndDate) + 1) as DaysCount
                FROM Leaves
                WHERE EmployeeId = {employee_id}
                AND Status = 'Approved'
                AND DATEDIFF(day, StartDate, EndDate) <= 30
                GROUP BY YEAR(StartDate), Type
                ORDER BY Year DESC
            """
            any_df = pd.read_sql(any_year_query, conn)
            if not any_df.empty:
                latest_year = any_df['Year'].max()
                for _, row in any_df[any_df['Year'] == latest_year].iterrows():
                    if row['Type'] == 'Vacation':
                        vacation_days = row['DaysCount']
                    elif row['Type'] == 'Sick':
                        sick_days = row['DaysCount']
                total_days = vacation_days + sick_days
        
        dept_query = f"""
            SELECT ISNULL(d.DepartmentId, 0) as DepartmentId
            FROM Employees e
            LEFT JOIN Departments d ON d.DepartmentId = e.DepartmentId
            WHERE e.EmployeeId = {employee_id}
        """
        dept_df = pd.read_sql(dept_query, conn)
        department_code = dept_df['DepartmentId'].iloc[0] if not dept_df.empty else 0
        
        years_query = f"""
            SELECT MIN(WorkDate) as FirstDate
            FROM Attendance
            WHERE EmployeeId = {employee_id}
        """
        years_df = pd.read_sql(years_query, conn)
        if not years_df.empty and years_df['FirstDate'].iloc[0]:
            first_date = pd.to_datetime(years_df['FirstDate'].iloc[0])
            years_worked = (datetime.now() - first_date).days / 365
        else:
            years_worked = 2
        
        conn.close()
        
        return {
            'prev_total_days': total_days,
            'prev_vacation_days': vacation_days,
            'prev_sick_days': sick_days,
            'years_worked': years_worked,
            'department_code': department_code,
            'has_data': total_days > 0
        }
        
    except Exception as e:
        print(f"Ошибка: {e}")
        conn.close()
        return {
            'prev_total_days': 10,
            'prev_vacation_days': 7,
            'prev_sick_days': 3,
            'years_worked': 2,
            'department_code': 0,
            'has_data': False
        }

def predict_for_employee(employee_id: int, year: int, history: dict) -> dict:
    features = np.array([[
        history['prev_total_days'],
        history['prev_vacation_days'],
        history['prev_sick_days'],
        history['years_worked'],
        history['department_code']
    ]])
    
    if model_manager.scaler:
        features_scaled = model_manager.scaler.transform(features)
    else:
        features_scaled = features
    
    try:
        total_days = model_manager.model.predict(features_scaled)[0]
    except:
        total_days = history['prev_total_days'] * 0.9 + 2
    
    total_days = max(2, min(35, total_days))
    
    if history['prev_total_days'] > 0:
        vacation_ratio = history['prev_vacation_days'] / history['prev_total_days']
    else:
        vacation_ratio = 0.7
    
    vacation_days = total_days * vacation_ratio
    sick_days = total_days * (1 - vacation_ratio)
    
    if total_days <= 8:
        risk_category = "Низкий"
        risk_score = 0.2
    elif total_days <= 15:
        risk_category = "Средний"
        risk_score = 0.5
    else:
        risk_category = "Высокий"
        risk_score = 0.8
    
    recommendations = []
    if history['has_data']:
        recommendations.append(f"На основе данных: {history['prev_total_days']:.0f} дней в прошлом году")
    else:
        recommendations.append("Нет данных за прошлый год - используется среднее значение")
    
    if total_days > 15:
        recommendations.append("Высокий риск - рекомендуется запланировать замену")
    elif total_days < 5:
        recommendations.append("Низкий риск - сотрудник стабилен")
    else:
        recommendations.append("Средний риск - рекомендуется мониторинг")
    
    return {
        'predicted_total_days': round(total_days, 1),
        'predicted_vacation_days': round(vacation_days, 1),
        'predicted_sick_days': round(sick_days, 1),
        'risk_category': risk_category,
        'risk_score': risk_score,
        'recommendations': recommendations
    }

class PredictionRequest(BaseModel):
    employee_id: int = Field(..., description="ID сотрудника")
    year: int = Field(..., description="Год для прогноза", ge=2024, le=2030)

class PredictionResponse(BaseModel):
    employee_id: int
    predicted_total_days: float
    predicted_vacation_days: float
    predicted_sick_days: float
    risk_category: str
    risk_score: float
    recommendations: List[str]

class BatchPredictionRequest(BaseModel):
    employees: List[int]
    year: int

class HealthResponse(BaseModel):
    status: str
    model_loaded: bool
    model_type: str
    train_date: str
    metrics: dict

@app.get("/")
async def root():
    return {
        "service": "Leave Prediction Service",
        "version": "1.0",
        "model_type": "Linear Regression",
        "endpoints": {
            "predict": "POST /api/v1/predict",
            "batch_predict": "POST /api/v1/predict/batch",
            "health": "GET /api/v1/health"
        }
    }

@app.post("/api/v1/predict", response_model=PredictionResponse)
async def predict(request: PredictionRequest):
    try:
        history = get_employee_history(request.employee_id, request.year)
        result = predict_for_employee(request.employee_id, request.year, history)
        return PredictionResponse(employee_id=request.employee_id, **result)
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

@app.post("/api/v1/predict/batch")
async def predict_batch(request: BatchPredictionRequest):
    results = []
    for emp_id in request.employees:
        try:
            history = get_employee_history(emp_id, request.year)
            result = predict_for_employee(emp_id, request.year, history)
            results.append({"employee_id": emp_id, **result})
        except Exception as e:
            results.append({"employee_id": emp_id, "error": str(e)})
    return {
        "year": request.year,
        "total": len(request.employees),
        "successful": len([r for r in results if 'error' not in r]),
        "results": results
    }

@app.get("/api/v1/health", response_model=HealthResponse)
async def health():
    metadata = model_manager.metadata if model_manager.metadata else {}
    return HealthResponse(
        status="healthy",
        model_loaded=model_manager.model is not None,
        model_type="Linear Regression",
        train_date=metadata.get('train_date', 'unknown'),
        metrics={
            "mae": metadata.get('mae', 0),
            "r2": metadata.get('r2', 0)
        }
    )

if __name__ == "__main__":
    print("\nЗапуск сервера на http://localhost:8000")
    print("Документация: http://localhost:8000/docs")
    uvicorn.run(app, host="0.0.0.0", port=8000)