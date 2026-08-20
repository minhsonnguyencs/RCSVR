pip install pandas

import pandas as pd
import numpy as np

# 1. Load Raw CSV Dataset
df = pd.read_csv('Trial_Master_Benchmark_Results.csv')

# Clean missing profiler entries if running without development build flags
df.replace(-1.0, np.nan, inplace=True)

# 2. Aggregation Helper Functions
def p99(x):
    return np.percentile(x, 99)

def reprojection_rate(x):
    return (x == True).mean() * 100.0

# 3. Group by Unique Test Conditions (LOD, Building Count, Vehicle Count)
summary = df.groupby(['LOD', 'BuildingCount', 'VehicleCount']).agg(
    Mean_FPS=('FPS', 'mean'),
    Std_FPS=('FPS', 'std'),
    Mean_FrameTime_ms=('DeltaTimeMS', 'mean'),
    P99_FrameTime_ms=('DeltaTimeMS', p99), # Korhonen extension: 99th percentile frame time
    Reprojection_Rate_Pct=('IsReprojected', reprojection_rate), # Korhonen extension: Reprojection rate
    Mean_CPU_Time_ms=('CpuTimeMS', 'mean'),
    Mean_GPU_Time_ms=('GpuTimeMS', 'mean'),
    Mean_RAM_MB=('AllocatedRAM_MB', 'mean'),
    Max_Thermal_Temp_C=('ThermalTempC', 'max') # Track maximum device temperature
).reset_index()

# 4. Save Final Thesis Summary Table
summary.to_csv('Aggregated_Performance_Summary.csv', index=False)
print("Analysis complete. Aggregated data saved to Aggregated_Performance_Summary.csv")