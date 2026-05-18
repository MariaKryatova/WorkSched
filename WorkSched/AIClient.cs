using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace WorkSched
{
    public class AIClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        public AIClient(string baseUrl = "http://localhost:8000")
        {
            _httpClient = new HttpClient();
            _baseUrl = baseUrl;
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public async Task<bool> IsHealthyAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/api/v1/health");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<PredictionResult> PredictAsync(int employeeId, int year)
        {
            var request = new PredictionRequest
            {
                employee_id = employeeId,
                year = year
            };

            var json = JsonConvert.SerializeObject(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_baseUrl}/api/v1/predict", content);
            var responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Ошибка API: {responseJson}");
            }

            return JsonConvert.DeserializeObject<PredictionResult>(responseJson);
        }

        public async Task<BatchPredictionResult> PredictBatchAsync(List<int> employeeIds, int year)
        {
            var request = new BatchPredictionRequest
            {
                employees = employeeIds,
                year = year
            };

            var json = JsonConvert.SerializeObject(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_baseUrl}/api/v1/predict/batch", content);
            var responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Ошибка API: {responseJson}");
            }

            return JsonConvert.DeserializeObject<BatchPredictionResult>(responseJson);
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    public class PredictionRequest
    {
        public int employee_id { get; set; }
        public int year { get; set; }
    }

    public class PredictionResult
    {
        public int employee_id { get; set; }
        public float predicted_total_days { get; set; }
        public float predicted_vacation_days { get; set; }
        public float predicted_sick_days { get; set; }
        public string risk_category { get; set; }
        public float risk_score { get; set; }
        public List<string> recommendations { get; set; }
    }

    public class BatchPredictionRequest
    {
        public List<int> employees { get; set; }
        public int year { get; set; }
    }

    public class BatchPredictionResult
    {
        public int year { get; set; }
        public int total { get; set; }
        public int successful { get; set; }
        public List<PredictionResult> results { get; set; }
    }
}