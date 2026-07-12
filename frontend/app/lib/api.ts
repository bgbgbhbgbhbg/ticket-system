/**
 * API 端點配置
 */

const API_BASE_URL = (process.env.NEXT_PUBLIC_API_URL ? process.env.NEXT_PUBLIC_API_URL + '/api' : 'http://localhost:5263/api');
const API_VERSION = 'v1';

export const API_ENDPOINTS = {
  // Task 1: Tickets endpoints
  tickets: {
    list: {
      path: `/tickets`,
      method: 'GET',
      name: 'GetTickets',
      fullUrl: `${API_BASE_URL}/${API_VERSION}/tickets`,
    },
    detail: (id: string) => ({
      path: `/tickets/${id}`,
      method: 'GET',
      name: 'GetTicketById',
      fullUrl: `${API_BASE_URL}/${API_VERSION}/tickets/${id}`,
    }),
  },
} as const;

// 票券相關型別定義
export interface Ticket {
  id: string;
  name: string;
  eventName: string;
  eventStartAt: string;
  price: number;
  availableQuantity: number;
}

export const apiClient = {

  /**
   * 取得所有票券列表
   */
  async getAllTickets(): Promise<Ticket[]> {
    const response = await fetch(API_ENDPOINTS.tickets.list.fullUrl, {
      method: 'GET',
      headers: {
        'Content-Type': 'application/json',
      },
    });

    if (!response.ok) {
      throw new Error(`Failed to fetch tickets: ${response.statusText}`);
    }

    return response.json();
  },

  /**
   * 取得單一票券詳情
   */
  async getTicketById(id: string): Promise<Ticket> {
    const endpoint = API_ENDPOINTS.tickets.detail(id);
    const response = await fetch(endpoint.fullUrl, {
      method: 'GET',
      headers: {
        'Content-Type': 'application/json',
      },
    });

    if (!response.ok) {
      if (response.status === 404) {
        throw new Error('找不到此票券');
      }
      throw new Error(`Failed to fetch ticket: ${response.statusText}`);
    }

    return response.json();
  },
};
