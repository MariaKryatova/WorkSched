import pyodbc
import pandas as pd
import numpy as np
import matplotlib.pyplot as plt
from sklearn.model_selection import train_test_split
from sklearn.linear_model import LinearRegression
from sklearn.ensemble import RandomForestRegressor
from sklearn.preprocessing import StandardScaler, LabelEncoder
from sklearn.metrics import mean_absolute_error, mean_squared_error, r2_score
import joblib
import os

os.makedirs('models', exist_ok=True)
os.makedirs('reports', exist_ok=True)

print("3. МОДЕЛИРОВАНИЕ И ОЦЕНКА")

def get_connection():
    conn_str = (
        r'DRIVER={SQL Server};'
        r'SERVER=DIABWIX\SQLEXPRESS;'
        r'DATABASE=WorkSched;'
        r'Trusted_Connection=yes;'
    )
    return pyodbc.connect(conn_str)

conn = get_connection()
query = """
    SELECT 
        l.EmployeeId,
        l.Type,
        l.StartDate,
        l.EndDate,
        DATEDIFF(day, l.StartDate, l.EndDate) + 1 as DaysCount
    FROM Leaves l
    WHERE l.Status = 'Approved'
    AND DATEDIFF(day, l.StartDate, l.EndDate) <= 30
"""
df = pd.read_sql(query, conn)

if df.empty:
    print(" Нет данных для обучения!")
    exit()

print(f"\n Загружено заявок: {len(df)}")

dept_query = """
    SELECT e.EmployeeId, ISNULL(d.Name, 'Unknown') as Department
    FROM Employees e
    LEFT JOIN Departments d ON d.DepartmentId = e.DepartmentId
"""
dept_df = pd.read_sql(dept_query, conn)
conn.close()

df['Year'] = pd.to_datetime(df['StartDate']).dt.year
aggregated = df.groupby(['EmployeeId', 'Year', 'Type'])['DaysCount'].sum().reset_index()

pivot = aggregated.pivot_table(
    index=['EmployeeId', 'Year'],
    columns='Type',
    values='DaysCount',
    fill_value=0
).reset_index()

if 'Vacation' in pivot.columns:
    pivot = pivot.rename(columns={'Vacation': 'VacationDays'})
else:
    pivot['VacationDays'] = 0

if 'Sick' in pivot.columns:
    pivot = pivot.rename(columns={'Sick': 'SickDays'})
else:
    pivot['SickDays'] = 0

pivot['TotalDays'] = pivot['VacationDays'] + pivot['SickDays']
pivot = pivot.sort_values(['EmployeeId', 'Year'])

pivot['PrevTotalDays'] = pivot.groupby('EmployeeId')['TotalDays'].shift(1)
pivot['PrevVacationDays'] = pivot.groupby('EmployeeId')['VacationDays'].shift(1)
pivot['PrevSickDays'] = pivot.groupby('EmployeeId')['SickDays'].shift(1)
pivot = pivot.dropna(subset=['PrevTotalDays'])

pivot = pivot.merge(dept_df, on='EmployeeId', how='left')
pivot['YearsWorked'] = pivot['Year'] - 2022
pivot['YearsWorked'] = pivot['YearsWorked'].clip(lower=0, upper=10)

le = LabelEncoder()
pivot['DepartmentCode'] = le.fit_transform(pivot['Department'].fillna('Unknown'))

feature_cols = ['PrevTotalDays', 'PrevVacationDays', 'PrevSickDays', 'YearsWorked', 'DepartmentCode']
X = pivot[feature_cols]
y = pivot['TotalDays']

print(f"\n Данные для обучения: {len(X)} записей")
print(f" Признаков: {len(feature_cols)}")

X_train, X_test, y_train, y_test = train_test_split(X, y, test_size=0.2, random_state=42)
print(f" Обучающая выборка: {len(X_train)} записей (80%)")
print(f" Тестовая выборка: {len(X_test)} записей (20%)")

scaler = StandardScaler()
X_train_scaled = scaler.fit_transform(X_train)
X_test_scaled = scaler.transform(X_test)

lr = LinearRegression()
lr.fit(X_train_scaled, y_train)
lr_pred = lr.predict(X_test_scaled)

rf = RandomForestRegressor(n_estimators=100, max_depth=10, random_state=42, n_jobs=-1)
rf.fit(X_train_scaled, y_train)
rf_pred = rf.predict(X_test_scaled)

print("РЕЗУЛЬТАТЫ СРАВНЕНИЯ МОДЕЛЕЙ")
print(f"{'Модель':<20} {'MAE':<10} {'MSE':<10} {'R²':<10}")

lr_mae = mean_absolute_error(y_test, lr_pred)
lr_mse = mean_squared_error(y_test, lr_pred)
lr_r2 = r2_score(y_test, lr_pred)
print(f"{'Линейная регрессия':<20} {lr_mae:<10.3f} {lr_mse:<10.3f} {lr_r2:<10.3f}")

rf_mae = mean_absolute_error(y_test, rf_pred)
rf_mse = mean_squared_error(y_test, rf_pred)
rf_r2 = r2_score(y_test, rf_pred)
print(f"{'Random Forest':<20} {rf_mae:<10.3f} {rf_mse:<10.3f} {rf_r2:<10.3f}")

if rf_r2 > lr_r2:
    best_model = rf
    best_name = "Random Forest"
    best_r2 = rf_r2
else:
    best_model = lr
    best_name = "Линейная регрессия"
    best_r2 = lr_r2

print(f" ЛУЧШАЯ МОДЕЛЬ: {best_name}")
print(f"   R² = {best_r2:.3f}")

joblib.dump(best_model, 'models/best_model.pkl')
joblib.dump(scaler, 'models/scaler.pkl')
joblib.dump(feature_cols, 'models/feature_names.pkl')
print("\n Модель сохранена: models/best_model.pkl")

plt.figure(figsize=(10, 6))
y_pred = best_model.predict(X_test_scaled)
plt.scatter(y_test, y_pred, alpha=0.5, color='steelblue')
plt.plot([y_test.min(), y_test.max()], [y_test.min(), y_test.max()], 'r--', lw=2)
plt.xlabel('Реальные значения (дни)', fontsize=12)
plt.ylabel('Предсказанные значения (дни)', fontsize=12)
plt.title(f'{best_name}: Предсказанные vs Реальные значения', fontsize=14)
plt.tight_layout()
plt.savefig('reports/model_comparison.png', dpi=150)
plt.close()
print(" График сохранен: reports/model_comparison.png")

print(" МОДЕЛИРОВАНИЕ ЗАВЕРШЕНО")