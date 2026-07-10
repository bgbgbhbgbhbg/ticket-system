/**
 * API 端點配置
 * 對應後端的 Program.cs 中的路由定義
 * #sym:weatherforecast - WeatherForecast 端點標識符
 */

const API_BASE_URL = process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5000/api';
const API_VERSION = 'v1';

export const API_ENDPOINTS = {
  // #sym:weatherforecast - 天氣預測端點
  weatherforecast: {
    path: `/weatherforecast`,
    method: 'GET',
    name: 'GetWeatherForecast',
    fullUrl: `${API_BASE_URL}/api/${API_VERSION}/weatherforecast`,
  },
} as const;

export const apiClient = {
  /**
   * 呼叫天氣預測 API
   * @returns 天氣預測資料陣列
   */
  async getWeatherForecast() {
    const response = await fetch(API_ENDPOINTS.weatherforecast.fullUrl);
    
    if (!response.ok) {
      throw new Error(
        `API Error: ${response.status} ${response.statusText} - ${API_ENDPOINTS.weatherforecast.name}`
      );
    }

    return response.json();
  },
};

export type WeatherForecast = {
  date: string;
  temperatureC: number;
  temperatureF: number;
  summary: string;
};
