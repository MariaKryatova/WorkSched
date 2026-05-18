import pandas as pd
import numpy as np
import pyodbc
from sklearn.linear_model import LinearRegression
from sklearn.model_selection import train_test_split
from sklearn.metrics import mean_absolute_error, r2_score, mean_squared_error
from sklearn.preprocessing import StandardScaler, LabelEncoder
import joblib
import os
from datetime import datetime
import matplotlib.pyplot as plt

def get_connection():
    conn_str = (
        r'DRIVER={SQL Server};'
        r'SERVER=DIABWIX\SQLEXPRESS;'
        r'DATABASE=WorkSched;'
        r'Trusted_Connection=yes;'
    )
    return pyodbc.connect(conn_str)

def collect_data_from_db():
    print("\n1. Сбор данных из базы данных...")
    
    conn = get_connection()
    
    query = """
        SELECT 
            l.EmployeeId,
            e.FullName,
            d.Name as Department,
            l.Type,
            l.StartDate,
            l.EndDate,
            YEAR(l.StartDate) as Year,
            DATEDIFF(day, l.StartDate, l.EndDate) + 1 as DaysCount
        FROM Leaves l
        JOIN Employees e ON e.EmployeeId = l.EmployeeId
        LEFT JOIN Departments d ON d.DepartmentId = e.DepartmentId
        WHERE l.Status = 'Approved'
        AND DATEDIFF(day, l.StartDate, l.EndDate) <= 30
        ORDER BY l.EmployeeId, l.StartDate
    """
    
    leaves_df = pd.read_sql(query, conn)
    print(f"   Загружено заявок: {len(leaves_df)}")
    
    if leaves_df.empty:
        print("   Нет данных в базе!")
        conn.close()
        return None
    
    attendance_query = """
        SELECT 
            EmployeeId,
            MIN(WorkDate) as FirstWorkDate
        FROM Attendance
        GROUP BY EmployeeId
    """
    
    attendance_df = pd.read_sql(attendance_query, conn)
    
    conn.close()
    
    print("\n2. Агрегация данных по годам...")
    
    pivot = leaves_df.pivot_table(
        index=['EmployeeId', 'FullName', 'Department', 'Year'],
        columns='Type',
        values='DaysCount',
        fill_value=0,
        aggfunc='sum'
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
    
    pivot['YearsWorked'] = pivot.apply(
        lambda row: get_years_worked(row['EmployeeId'], row['Year'], attendance_df), 
        axis=1
    )
    
    le = LabelEncoder()
    pivot['DepartmentCode'] = le.fit_transform(pivot['Department'].fillna('Unknown'))
    
    print(f"   Сформировано записей для обучения: {len(pivot)}")
    print(f"   Диапазон лет: {pivot['Year'].min()} - {pivot['Year'].max()}")
    
    return pivot

def get_years_worked(employee_id, current_year, attendance_df):
    emp_data = attendance_df[attendance_df['EmployeeId'] == employee_id]
    if emp_data.empty:
        return max(0, current_year - 2022)
    
    first_date = pd.to_datetime(emp_data['FirstWorkDate'].iloc[0])
    first_year = first_date.year
    return max(0, current_year - first_year)

def create_synthetic_data():
    print("\n2. Создание синтетических данных...")
    
    np.random.seed(42)
    
    departments = {
        'IT': {'base_vacation': 12, 'base_sick': 3},
        'Sales': {'base_vacation': 15, 'base_sick': 5},
        'HR': {'base_vacation': 10, 'base_sick': 4}
    }
    
    employees = range(1, 51)
    years = [2022, 2023, 2024]
    
    data = []
    for emp in employees:
        dept = np.random.choice(['IT', 'Sales', 'HR'])
        dept_params = departments[dept]
        
        prev_days = 0
        for year in years:
            trend = (year - 2022) * 0.5
            
            vacation = max(0, dept_params['base_vacation'] + trend + np.random.normal(0, 2))
            sick = max(0, dept_params['base_sick'] + np.random.normal(0, 1.5))
            
            total = vacation + sick
            
            data.append({
                'EmployeeId': emp,
                'Department': dept,
                'Year': year,
                'VacationDays': round(vacation, 1),
                'SickDays': round(sick, 1),
                'TotalDays': round(total, 1),
                'PrevTotalDays': prev_days if prev_days > 0 else total,
                'PrevVacationDays': vacation if prev_days == 0 else vacation,
                'PrevSickDays': sick if prev_days == 0 else sick,
                'YearsWorked': year - 2020,
                'DepartmentCode': 0 if dept == 'IT' else 1 if dept == 'Sales' else 2
            })
            
            prev_days = total
    
    df = pd.DataFrame(data)
    print(f"   Создано записей: {len(df)}")
    
    return df

def prepare_data(df):
    print("\n3. Подготовка данных для обучения...")
    
    feature_columns = [
        'PrevTotalDays',
        'PrevVacationDays',
        'PrevSickDays',
        'YearsWorked',
        'DepartmentCode'
    ]
    
    target_column = 'TotalDays'
    
    X = df[feature_columns]
    y = df[target_column]
    
    print(f"   Признаков: {len(feature_columns)}")
    print(f"   Образцов: {len(X)}")
    print(f"\n   Признаки:")
    for col in feature_columns:
        print(f"      - {col}: min={X[col].min():.1f}, max={X[col].max():.1f}, mean={X[col].mean():.1f}")
    
    return X, y, feature_columns

def train_linear_regression(X, y, feature_names):
    print("\n4. Обучение модели линейной регрессии...")
    
    X_train, X_test, y_train, y_test = train_test_split(
        X, y, test_size=0.2, random_state=42
    )
    
    print(f"   Обучающая выборка: {len(X_train)} записей")
    print(f"   Тестовая выборка: {len(X_test)} записей")
    
    scaler = StandardScaler()
    X_train_scaled = scaler.fit_transform(X_train)
    X_test_scaled = scaler.transform(X_test)
    
    model = LinearRegression()
    model.fit(X_train_scaled, y_train)
    
    y_train_pred = model.predict(X_train_scaled)
    y_test_pred = model.predict(X_test_scaled)
    
    train_mae = mean_absolute_error(y_train, y_train_pred)
    test_mae = mean_absolute_error(y_test, y_test_pred)
    train_r2 = r2_score(y_train, y_train_pred)
    test_r2 = r2_score(y_test, y_test_pred)
    rmse = np.sqrt(mean_squared_error(y_test, y_test_pred))
    
    print(f"\n   РЕЗУЛЬТАТЫ ОБУЧЕНИЯ:")
    print(f"   Средняя ошибка (MAE):     {test_mae:.2f} дней")
    print(f"   Корень из MSE (RMSE):     {rmse:.2f} дней")
    print(f"   R² (коэф. детерминации):  {test_r2:.3f}")
    
    print(f"\n   КОЭФФИЦИЕНТЫ МОДЕЛИ:")
    print(f"   Свободный член (intercept): {model.intercept_:.2f}")
    for name, coef in zip(feature_names, model.coef_):
        print(f"   {name:20} : {coef:8.2f}")
    
    print(f"\n   ИНТЕРПРЕТАЦИЯ:")
    for name, coef in zip(feature_names, model.coef_):
        effect = "увеличивает" if coef > 0 else "уменьшает"
        print(f"   • {name}: {effect} прогноз на {abs(coef):.2f} дней")
    
    return model, scaler, test_mae, test_r2

def plot_results(y_test, y_pred, model, feature_names, coeffs):
    print("\n5. Построение графиков...")
    
    fig, axes = plt.subplots(1, 3, figsize=(15, 4))
    
    axes[0].scatter(y_test, y_pred, alpha=0.5)
    axes[0].plot([y_test.min(), y_test.max()], [y_test.min(), y_test.max()], 'r--', lw=2)
    axes[0].set_xlabel('Реальные значения (дни)')
    axes[0].set_ylabel('Предсказанные значения (дни)')
    axes[0].set_title('Предсказанные vs Реальные значения')
    axes[0].grid(True, alpha=0.3)
    
    residuals = y_test - y_pred
    axes[1].scatter(y_pred, residuals, alpha=0.5)
    axes[1].axhline(y=0, color='r', linestyle='--')
    axes[1].set_xlabel('Предсказанные значения (дни)')
    axes[1].set_ylabel('Остатки (дни)')
    axes[1].set_title('График остатков')
    axes[1].grid(True, alpha=0.3)
    
    axes[2].barh(feature_names, coeffs)
    axes[2].set_xlabel('Коэффициент')
    axes[2].set_title('Важность признаков')
    axes[2].axvline(x=0, color='black', linestyle='-', linewidth=0.5)
    axes[2].grid(True, alpha=0.3)
    
    plt.tight_layout()
    plt.savefig('models/model_analysis.png', dpi=150)
    print("   Графики сохранены в models/model_analysis.png")
    plt.show()

def save_model(model, scaler, feature_names, mae, r2):
    print("\n6. Сохранение модели...")
    
    os.makedirs('models', exist_ok=True)
    
    joblib.dump(model, 'models/linear_regression_model.pkl')
    joblib.dump(scaler, 'models/scaler.pkl')
    joblib.dump(feature_names, 'models/feature_names.pkl')
    
    metadata = {
        'model_type': 'LinearRegression',
        'train_date': datetime.now().strftime('%Y-%m-%d %H:%M:%S'),
        'mae': mae,
        'r2': r2,
        'feature_names': feature_names
    }
    joblib.dump(metadata, 'models/metadata.pkl')
    
    print(f"   Модель: models/linear_regression_model.pkl")
    print(f"   Масштабатор: models/scaler.pkl")
    print(f"   Признаки: models/feature_names.pkl")
    print(f"   Метаданные: models/metadata.pkl")

def test_model(model, scaler, feature_names):
    print("\n7. Тестирование модели...")
    
    test_data = np.array([[
        12,
        10,
        2,
        3,
        0
    ]])
    
    test_scaled = scaler.transform(test_data)
    prediction = model.predict(test_scaled)[0]
    
    print(f"\n   ПРИМЕР ПРЕДСКАЗАНИЯ:")
    print(f"   Входные данные:")
    print(f"     • Отпуск в прошлом году: 10 дней")
    print(f"     • Больничный в прошлом: 2 дня")
    print(f"     • Всего в прошлом: 12 дней")
    print(f"     • Стаж: 3 года")
    print(f"     • Отдел: IT (код 0)")
    print(f"   ПРЕДСКАЗАНИЕ: {prediction:.1f} дней в следующем году")

def main():
    try:
        df = collect_data_from_db()
        
        if df is None or len(df) < 20:
            print("\nНедостаточно данных в базе!")
            df = create_synthetic_data()
        
        X, y, feature_names = prepare_data(df)
        
        model, scaler, mae, r2 = train_linear_regression(X, y, feature_names)
        
        save_model(model, scaler, feature_names, mae, r2)
        
        test_model(model, scaler, feature_names)
        
        X_train, X_test, y_train, y_test = train_test_split(X, y, test_size=0.2, random_state=42)
        X_test_scaled = scaler.transform(X_test)
        y_pred = model.predict(X_test_scaled)
        
        plot_results(y_test, y_pred, model, feature_names, model.coef_)
        
        print("\n" + "=" * 70)
        print("ОБУЧЕНИЕ ЗАВЕРШЕНО УСПЕШНО!")
        print("=" * 70)
        print("\nФормула прогноза:")
        formula = f"Прогноз = {model.intercept_:.2f}"
        for name, coef in zip(feature_names, model.coef_):
            formula += f" + ({coef:.2f} × {name})"
        print(f"   {formula}")
        
    except Exception as e:
        print(f"\nОШИБКА: {e}")
        import traceback
        traceback.print_exc()

if __name__ == "__main__":
    main()