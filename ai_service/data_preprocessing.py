import pyodbc
import pandas as pd
from sklearn.preprocessing import LabelEncoder
import os

os.makedirs('reports', exist_ok=True)

print("2. ПРЕДОБРАБОТКА ДАННЫХ И ИНЖИНИРИНГ ПРИЗНАКОВ")

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
        e.FullName,
        ISNULL(d.Name, 'Unknown') as Department,
        l.Type,
        l.StartDate,
        l.EndDate,
        DATEDIFF(day, l.StartDate, l.EndDate) + 1 as DaysCount
    FROM Leaves l
    JOIN Employees e ON e.EmployeeId = l.EmployeeId
    LEFT JOIN Departments d ON d.DepartmentId = e.DepartmentId
    WHERE l.Status = 'Approved'
    AND DATEDIFF(day, l.StartDate, l.EndDate) <= 30
"""
df = pd.read_sql(query, conn)
conn.close()

print(f"\n Загружено заявок: {len(df)}")

if df.empty:
    print(" Нет данных в таблице Leaves!")
    exit()

df['Year'] = pd.to_datetime(df['StartDate']).dt.year
aggregated = df.groupby(['EmployeeId', 'FullName', 'Department', 'Year', 'Type'])['DaysCount'].sum().reset_index()

pivot = aggregated.pivot_table(
    index=['EmployeeId', 'FullName', 'Department', 'Year'],
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

pivot['YearsWorked'] = pivot['Year'] - 2022
pivot['YearsWorked'] = pivot['YearsWorked'].clip(lower=0, upper=10)

le = LabelEncoder()
pivot['DepartmentCode'] = le.fit_transform(pivot['Department'].fillna('Unknown'))

print(f"\n Итоговый датасет: {len(pivot)} записей для обучения")
print(f"\n ПРИЗНАКИ:")
print(f"   • PrevTotalDays - всего дней в прошлом году")
print(f"   • PrevVacationDays - дней отпуска в прошлом году")
print(f"   • PrevSickDays - дней больничного в прошлом году")
print(f"   • YearsWorked - стаж работы (лет)")
print(f"   • DepartmentCode - код отдела (0=IT, 1=Sales, 2=HR)")
print(f"\n ЦЕЛЕВАЯ ПЕРЕМЕННАЯ:")
print(f"   • TotalDays - дней в прогнозируемом году")

print(f"\n ПРИМЕР ДАННЫХ:")
print(pivot[['FullName', 'Year', 'PrevTotalDays', 'TotalDays', 'YearsWorked', 'DepartmentCode']].head(10).to_string())

pivot.to_csv('reports/processed_dataset.csv', index=False)
print(f"\n Датасет сохранен: reports/processed_dataset.csv")

print(" ПРЕДОБРАБОТКА ЗАВЕРШЕНА")