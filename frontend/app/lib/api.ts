/**
 * API 端點配置
 */

const API_BASE_URL = process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5263/api/v1';

export const API_ENDPOINTS = {
  // Task 1: Tickets endpoints
  tickets: {
    list: {
      path: `/tickets`,
      method: 'GET',
      name: 'GetTickets',
      fullUrl: `${API_BASE_URL}/tickets`,
    },
    detail: (id: string) => ({
      path: `/tickets/${id}`,
      method: 'GET',
      name: 'GetTicketById',
      fullUrl: `${API_BASE_URL}/tickets/${id}`,
    }),
    // Task 8: 即時庫存（Cache-Aside）
    inventory: (id: string) => ({
      path: `/tickets/${id}/inventory`,
      method: 'GET',
      name: 'GetTicketInventory',
      fullUrl: `${API_BASE_URL}/tickets/${id}/inventory`,
    }),
  },
  // Task 4: Orders endpoints
  orders: {
    create: {
      path: `/orders`,
      method: 'POST',
      name: 'CreateOrder',
      fullUrl: `${API_BASE_URL}/orders`,
    },
    detail: (id: string) => ({
      path: `/orders/${id}`,
      method: 'GET',
      name: 'GetOrderById',
      fullUrl: `${API_BASE_URL}/orders/${id}`,
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

// 庫存查詢回應（Task 8 Cache-Aside，對應 api-spec.yaml inventory endpoint）
export interface InventoryResponse {
  ticketId: string;
  availableQuantity: number;
  cacheHit: boolean;
}

// 訂單相關型別定義（對應 api-spec.yaml OrderResponse schema）
export interface Order {
  id: string;
  ticketId: string;
  quantity: number;
  totalAmount: number;
  status: 'Pending' | 'Processing' | 'Success' | 'Failed';
  createdAt: string;
  updatedAt: string;
}

export interface CreateOrderPayload {
  ticketId: string;
  quantity: number;
}

export interface ApiError {
  errorCode: string;
  message: string;
  traceId?: string;
}

// Admin 分頁訂單回應（對應 api-spec.yaml Admin 區塊）
export interface PagedOrderResponse {
  items: Order[];
  total: number;
  page: number;
  pageSize: number;
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

  /**
   * 查詢票券即時庫存（Cache-Aside）
   * GET /tickets/{id}/inventory
   * 回傳 availableQuantity 與 cacheHit，對應 cache-strategy.md 第 3、6 節
   */
  async getTicketInventory(id: string): Promise<InventoryResponse> {
    const endpoint = API_ENDPOINTS.tickets.inventory(id);
    const response = await fetch(endpoint.fullUrl, {
      method: 'GET',
      headers: {
        'Content-Type': 'application/json',
      },
      // 不 cache：庫存是高頻異動資料，需要每次都打後端（後端自己有 Redis Cache）
      cache: 'no-store',
    });

    if (!response.ok) {
      if (response.status === 404) {
        throw new Error('找不到此票券');
      }
      throw new Error(`Failed to fetch inventory: ${response.statusText}`);
    }

    return response.json();
  },

  /**
   * 建立訂單（POST /orders）
   * @param token JWT access token
   * @param idempotencyKey 前端每次送出時產生的 UUID，防止重複訂單
   * @param payload ticketId + quantity
   */
  async createOrder(token: string, idempotencyKey: string, payload: CreateOrderPayload): Promise<Order> {
    const response = await fetch(API_ENDPOINTS.orders.create.fullUrl, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${token}`,
        'Idempotency-Key': idempotencyKey,
      },
      body: JSON.stringify(payload),
    });

    // 202 = 新訂單建立成功；409 = Idempotency-Key 重複（回傳原訂單 OrderResponse，對應 api-spec.yaml）
    if (response.status === 202 || response.status === 409) {
      return response.json();
    }

    const error: ApiError = await response.json();
    throw error;
  },

  /**
   * 查詢單筆訂單狀態（GET /orders/{id}）
   * @param token JWT access token
   * @param orderId 訂單 ID
   */
  async getOrderById(token: string, orderId: string): Promise<Order> {
    const endpoint = API_ENDPOINTS.orders.detail(orderId);
    const response = await fetch(endpoint.fullUrl, {
      method: 'GET',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${token}`,
      },
    });

    if (!response.ok) {
      if (response.status === 404) {
        throw new Error('找不到此訂單');
      }
      throw new Error(`Failed to fetch order: ${response.statusText}`);
    }

    return response.json();
  },

  /**
   * Admin 查詢所有訂單（GET /admin/orders）
   */
  async adminGetOrders(
    token: string,
    params: { status?: string; page?: number; pageSize?: number }
  ): Promise<PagedOrderResponse> {
    const query = new URLSearchParams();
    if (params.status) query.set('status', params.status);
    if (params.page) query.set('page', String(params.page));
    if (params.pageSize) query.set('pageSize', String(params.pageSize));

    const response = await fetch(`${API_BASE_URL}/admin/orders?${query}`, {
      method: 'GET',
      headers: { 'Content-Type': 'application/json', 'Authorization': `Bearer ${token}` },
    });

    if (!response.ok) {
      const error: ApiError = await response.json().catch(() => ({ errorCode: 'UNKNOWN', message: response.statusText }));
      throw error;
    }

    return response.json();
  },

  /**
   * Admin 手動更新訂單狀態（PATCH /admin/orders/{id}/status）
   */
  async adminUpdateOrderStatus(
    token: string,
    orderId: string,
    toStatus: 'Success' | 'Failed',
    reason: string
  ): Promise<Order> {
    const response = await fetch(`${API_BASE_URL}/admin/orders/${orderId}/status`, {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json', 'Authorization': `Bearer ${token}` },
      body: JSON.stringify({ toStatus, reason }),
    });

    if (!response.ok) {
      const error: ApiError = await response.json().catch(() => ({ errorCode: 'UNKNOWN', message: response.statusText }));
      throw error;
    }

    return response.json();
  },
};
