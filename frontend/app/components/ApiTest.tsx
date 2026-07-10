'use client';

import { useEffect, useState } from 'react';
import { apiClient, API_ENDPOINTS, type WeatherForecast } from '../lib/api';

export default function ApiTest() {
  const [forecast, setForecast] = useState<WeatherForecast[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const fetchWeatherForecast = async () => {
      try {
        setLoading(true);
        const data = await apiClient.getWeatherForecast();
        setForecast(data);
        setError(null);
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Unknown error occurred');
        setForecast([]);
      } finally {
        setLoading(false);
      }
    };

    fetchWeatherForecast();
  }, []);

  return (
    <div className="w-full max-w-2xl mx-auto p-6 bg-white dark:bg-zinc-900 rounded-lg shadow-md">
      <h2 className="text-2xl font-bold mb-4 text-zinc-900 dark:text-zinc-50">
        Backend API Response
      </h2>

      <div className="mb-4 text-sm text-zinc-600 dark:text-zinc-400 space-y-1">
        <p>API 端點: {API_ENDPOINTS.weatherforecast.name}</p>
        <p>完整 URL: {API_ENDPOINTS.weatherforecast.fullUrl}</p>
        <p className="text-xs text-zinc-500">標識符: #sym:weatherforecast</p>
      </div>

      {loading && (
        <div className="text-center py-8">
          <div className="inline-block animate-spin rounded-full h-8 w-8 border-b-2 border-zinc-900 dark:border-zinc-50"></div>
          <p className="mt-2 text-zinc-600 dark:text-zinc-400">Loading weather forecast...</p>
        </div>
      )}

      {error && (
        <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg p-4 text-red-800 dark:text-red-300">
          <p className="font-semibold">Error</p>
          <p className="text-sm">{error}</p>
          <p className="text-xs mt-2 text-red-600 dark:text-red-400">
            ℹ️ Make sure backend is running: <code className="bg-red-100 dark:bg-red-900 px-1 rounded">dotnet watch run</code>
          </p>
        </div>
      )}

      {forecast.length > 0 && (
        <div className="overflow-x-auto">
          <table className="w-full text-sm border-collapse">
            <thead className="bg-zinc-100 dark:bg-zinc-800">
              <tr>
                <th className="border border-zinc-300 dark:border-zinc-700 px-4 py-2 text-left font-semibold text-zinc-900 dark:text-zinc-50">
                  Date
                </th>
                <th className="border border-zinc-300 dark:border-zinc-700 px-4 py-2 text-left font-semibold text-zinc-900 dark:text-zinc-50">
                  Temp (°C)
                </th>
                <th className="border border-zinc-300 dark:border-zinc-700 px-4 py-2 text-left font-semibold text-zinc-900 dark:text-zinc-50">
                  Temp (°F)
                </th>
                <th className="border border-zinc-300 dark:border-zinc-700 px-4 py-2 text-left font-semibold text-zinc-900 dark:text-zinc-50">
                  Summary
                </th>
              </tr>
            </thead>
            <tbody>
              {forecast.map((day, index) => (
                <tr key={index} className="hover:bg-zinc-50 dark:hover:bg-zinc-800">
                  <td className="border border-zinc-300 dark:border-zinc-700 px-4 py-2 text-zinc-900 dark:text-zinc-50">
                    {new Date(day.date).toLocaleDateString()}
                  </td>
                  <td className="border border-zinc-300 dark:border-zinc-700 px-4 py-2 text-zinc-900 dark:text-zinc-50">
                    {day.temperatureC}
                  </td>
                  <td className="border border-zinc-300 dark:border-zinc-700 px-4 py-2 text-zinc-900 dark:text-zinc-50">
                    {day.temperatureF}
                  </td>
                  <td className="border border-zinc-300 dark:border-zinc-700 px-4 py-2 text-zinc-900 dark:text-zinc-50">
                    <span className="inline-block px-2 py-1 rounded bg-blue-100 dark:bg-blue-900/30 text-blue-900 dark:text-blue-300 text-xs font-medium">
                      {day.summary}
                    </span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {!loading && forecast.length === 0 && !error && (
        <div className="text-center py-8 text-zinc-500 dark:text-zinc-400">
          No data available
        </div>
      )}
    </div>
  );
}
