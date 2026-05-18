import pyodbc
import pandas as pd
import matplotlib.pyplot as plt
import seaborn as sns
import os

os.makedirs('reports', exist_ok=True)

print("1. АНАЛИЗ ПРЕДМЕТНОЙ ОБЛАСТИ")

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
        l.LeaveId,
        l.EmployeeId,
        e.FullName,
        d.Name as Department,
        l.Type,
        l.StartDate,
        l.EndDate,
        l.Status,
        DATEDIFF(day, l.StartDate, l.EndDate) + 1 as DaysCount
    FROM Leaves l
    JOIN Employees e ON e.EmployeeId = l.EmployeeId
    LEFT JOIN Departments d ON d.DepartmentId = e.DepartmentId
    WHERE l.Status = 'Approved'
    ORDER BY l.StartDate
"""
df = pd.read_sql(query, conn)
conn.close()

print(f"\n Загружено заявок: {len(df)}")

print(f"Колонки в данных: {list(df.columns)}")

if 'EmployeeId' in df.columns:
    print(f" Всего сотрудников: {df['EmployeeId'].nunique()}")
else:
    print(" Колонка 'EmployeeId' не найдена")

if 'StartDate' in df.columns:
    print(f" Период данных: {df['StartDate'].min()} - {df['StartDate'].max()}")
else:
    print(" Колонка 'StartDate' не найдена")

print(f"\n Пропуски в данных:")
print(df.isnull().sum())

print(f"\n Дубликаты: {df.duplicated().sum()}")

if 'FullName' in df.columns and 'DaysCount' in df.columns:
    plt.figure(figsize=(12, 6))
    emp_days = df.groupby('FullName')['DaysCount'].sum().sort_values(ascending=False)
    emp_days.plot(kind='bar', color='steelblue', edgecolor='black')
    plt.title('Распределение дней отпуска и больничного по сотрудникам', fontsize=14)
    plt.xlabel('Сотрудник', fontsize=12)
    plt.ylabel('Количество дней', fontsize=12)
    plt.xticks(rotation=45, ha='right')
    plt.tight_layout()
    plt.savefig('reports/employee_distribution.png', dpi=150)
    plt.close()
    print("\n Сохранен график: reports/employee_distribution.png")
else:
    print("\n Недостаточно данных для графика по сотрудникам")

if 'Type' in df.columns:
    plt.figure(figsize=(8, 8))
    type_counts = df['Type'].value_counts()
    plt.pie(type_counts.values, labels=type_counts.index, autopct='%1.1f%%', startangle=90)
    plt.title('Распределение заявок по типам', fontsize=14)
    plt.tight_layout()
    plt.savefig('reports/type_distribution.png', dpi=150)
    plt.close()
    print(" Сохранен график: reports/type_distribution.png")
else:
    print(" Недостаточно данных для графика по типам")

print("\n" + "=" * 70)
print(" АНАЛИЗ ЗАВЕРШЕН")
print("=" * 70)