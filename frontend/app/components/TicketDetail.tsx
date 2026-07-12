'use client';

import { useState, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { apiClient, type Ticket, type ApiError } from '@/app/lib/api';
import { useAuth } from '@/app/lib/auth-context';

interface TicketDetailProps {
  ticketId: string;
}

export default function TicketDetail({ ticketId }: TicketDetailProps) {
  const router = useRouter();
  const { user, token } = useAuth();
  const [ticket, setTicket] = useState<Ticket | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // 購票表單狀態
  const [quantity, setQuantity] = useState(1);
  const [submitting, setSubmitting] = useState(false);
  const [orderError, setOrderError] = useState<string | null>(null);

  useEffect(() => {
    async function loadTicket() {
      try {
        setLoading(true);
        const data = await apiClient.getTicketById(ticketId);
        setTicket(data);
        setError(null);
      } catch (err) {
        setError(err instanceof Error ? err.message : '載入失敗');
        console.error('Failed to load ticket:', err);
      } finally {
        setLoading(false);
      }
    }

    loadTicket();
  }, [ticketId]);

  async function handleOrder() {
    if (!token || !ticket) return;

    setSubmitting(true);
    setOrderError(null);

    // 每次點擊送出時產生新的 idempotency key，防止重複送出
    const idempotencyKey = crypto.randomUUID();

    try {
      const order = await apiClient.createOrder(token, idempotencyKey, {
        ticketId: ticket.id,
        quantity,
      });
      // 下單成功，跳轉到訂單狀態頁
      router.push(`/orders/${order.id}`);
    } catch (err) {
      const apiErr = err as ApiError;
      if (apiErr?.errorCode === 'ORDER_QUANTITY_EXCEEDS_LIMIT') {
        setOrderError('單筆最多購買 10 張');
      } else if (apiErr?.errorCode === 'TICKET_NOT_FOUND') {
        setOrderError('此票券不存在');
      } else if (apiErr?.message) {
        setOrderError(apiErr.message);
      } else {
        setOrderError('下單失敗，請稍後再試');
      }
    } finally {
      setSubmitting(false);
    }
  }

  if (loading) {
    return (
      <div className="flex justify-center items-center min-h-[400px]">
        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600"></div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="bg-red-50 border border-red-200 rounded-lg p-4">
        <p className="text-red-600">❌ {error}</p>
        <button
          onClick={() => router.push('/')}
          className="mt-4 text-blue-600 hover:underline"
        >
          ← 返回票券列表
        </button>
      </div>
    );
  }

  if (!ticket) {
    return (
      <div className="bg-gray-50 border border-gray-200 rounded-lg p-8 text-center">
        <p className="text-gray-600">找不到此票券</p>
        <button
          onClick={() => router.push('/')}
          className="mt-4 text-blue-600 hover:underline"
        >
          ← 返回票券列表
        </button>
      </div>
    );
  }

  return (
    <div className="max-w-2xl mx-auto">
      <button
        onClick={() => router.push('/')}
        className="mb-6 text-blue-600 hover:underline"
      >
        ← 返回票券列表
      </button>

      <div className="bg-white border border-gray-200 rounded-lg p-8 space-y-6">
        <div>
          <h1 className="text-3xl font-bold text-gray-900 mb-2">
            {ticket.eventName}
          </h1>
          <p className="text-xl text-gray-600">{ticket.name}</p>
        </div>

        <div className="border-t border-gray-200 pt-6 space-y-4">
          <div className="flex justify-between items-center">
            <span className="text-gray-600">票價</span>
            <span className="text-3xl font-bold text-blue-600">
              NT$ {ticket.price.toLocaleString()}
            </span>
          </div>

          <div className="flex justify-between items-center">
            <span className="text-gray-600">剩餘票數</span>
            <span className={`text-lg font-semibold ${
              ticket.availableQuantity > 50 ? 'text-green-600' :
              ticket.availableQuantity > 10 ? 'text-yellow-600' :
              'text-red-600'
            }`}>
              {ticket.availableQuantity} 張
            </span>
          </div>

          <div className="flex justify-between items-center">
            <span className="text-gray-600">活動時間</span>
            <span className="text-gray-900">
              {new Date(ticket.eventStartAt).toLocaleString('zh-TW', {
                year: 'numeric',
                month: '2-digit',
                day: '2-digit',
                hour: '2-digit',
                minute: '2-digit',
                weekday: 'short',
              })}
            </span>
          </div>
        </div>

        <div className="border-t border-gray-200 pt-6">
          {user && token ? (
            <div className="space-y-4">
              <div className="flex items-center gap-4">
                <label htmlFor="quantity" className="text-gray-700 font-medium whitespace-nowrap">
                  購買數量
                </label>
                <input
                  id="quantity"
                  type="number"
                  min={1}
                  max={10}
                  value={quantity}
                  onChange={(e) => setQuantity(Math.min(10, Math.max(1, parseInt(e.target.value) || 1)))}
                  className="w-24 border border-gray-300 rounded-md px-3 py-2 text-center focus:outline-none focus:ring-2 focus:ring-blue-500"
                />
                <span className="text-gray-500 text-sm">（最多 10 張）</span>
              </div>

              <div className="flex justify-between items-center text-sm text-gray-600">
                <span>小計</span>
                <span className="font-semibold text-gray-900">
                  NT$ {(ticket.price * quantity).toLocaleString()}
                </span>
              </div>

              {orderError && (
                <div className="bg-red-50 border border-red-200 rounded p-3">
                  <p className="text-red-600 text-sm">{orderError}</p>
                </div>
              )}

              <button
                onClick={handleOrder}
                disabled={submitting || ticket.availableQuantity === 0}
                className={`w-full py-3 px-6 rounded-lg font-semibold transition-colors ${
                  submitting || ticket.availableQuantity === 0
                    ? 'bg-gray-300 text-gray-500 cursor-not-allowed'
                    : 'bg-blue-600 hover:bg-blue-700 text-white'
                }`}
              >
                {submitting ? '處理中...' : ticket.availableQuantity === 0 ? '已售完' : '立即購票'}
              </button>
            </div>
          ) : (
            <div className="text-center space-y-3">
              <button
                onClick={() => router.push('/auth/login')}
                className="w-full bg-blue-600 hover:bg-blue-700 text-white py-3 px-6 rounded-lg font-semibold"
              >
                登入後購票
              </button>
              <p className="text-sm text-gray-500">
                還沒有帳號？
                <button
                  onClick={() => router.push('/auth/register')}
                  className="ml-1 text-blue-600 hover:underline"
                >
                  立即註冊
                </button>
              </p>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
