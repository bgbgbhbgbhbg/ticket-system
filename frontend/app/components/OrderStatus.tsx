'use client';

import { useState, useEffect, useCallback } from 'react';
import { useRouter } from 'next/navigation';
import { apiClient, type Order } from '@/app/lib/api';
import { useAuth } from '@/app/lib/auth-context';

const STATUS_LABELS: Record<Order['status'], string> = {
  Pending: '等待處理',
  Processing: '處理中',
  Success: '購票成功',
  Failed: '購票失敗',
};

const STATUS_COLORS: Record<Order['status'], string> = {
  Pending: 'text-yellow-600 bg-yellow-50 border-yellow-200',
  Processing: 'text-blue-600 bg-blue-50 border-blue-200',
  Success: 'text-green-600 bg-green-50 border-green-200',
  Failed: 'text-red-600 bg-red-50 border-red-200',
};

interface OrderStatusProps {
  orderId: string;
}

export default function OrderStatus({ orderId }: OrderStatusProps) {
  const router = useRouter();
  const { token } = useAuth();
  const [order, setOrder] = useState<Order | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const fetchOrder = useCallback(async () => {
    if (!token) return;
    try {
      const data = await apiClient.getOrderById(token, orderId);
      setOrder(data);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : '查詢失敗');
    } finally {
      setLoading(false);
    }
  }, [token, orderId]);

  useEffect(() => {
    fetchOrder();
  }, [fetchOrder]);

  // Polling：訂單在 Pending 或 Processing 時每 2 秒自動重新查詢
  useEffect(() => {
    if (!order) return;
    if (order.status === 'Success' || order.status === 'Failed') return;

    const timer = setInterval(fetchOrder, 2000);
    return () => clearInterval(timer);
  }, [order, fetchOrder]);

  if (!token) {
    return (
      <div className="bg-yellow-50 border border-yellow-200 rounded-lg p-6 text-center">
        <p className="text-yellow-700">請先登入才能查看訂單</p>
        <button
          onClick={() => router.push('/auth/login')}
          className="mt-4 bg-blue-600 text-white py-2 px-4 rounded hover:bg-blue-700"
        >
          前往登入
        </button>
      </div>
    );
  }

  if (loading) {
    return (
      <div className="flex justify-center items-center min-h-[300px]">
        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600"></div>
      </div>
    );
  }

  if (error || !order) {
    return (
      <div className="bg-red-50 border border-red-200 rounded-lg p-6">
        <p className="text-red-600">{error ?? '查無此訂單'}</p>
        <button
          onClick={() => router.push('/')}
          className="mt-4 text-blue-600 hover:underline"
        >
          ← 返回票券列表
        </button>
      </div>
    );
  }

  const isTerminal = order.status === 'Success' || order.status === 'Failed';

  return (
    <div className="max-w-lg mx-auto space-y-6">
      <button
        onClick={() => router.push('/')}
        className="text-blue-600 hover:underline"
      >
        ← 返回票券列表
      </button>

      <div className="bg-white border border-gray-200 rounded-lg p-8 space-y-6">
        <div className="text-center">
          <h2 className="text-2xl font-bold text-gray-900 mb-2">訂單狀態</h2>
          <span className={`inline-block px-4 py-2 rounded-full border font-semibold text-sm ${STATUS_COLORS[order.status]}`}>
            {STATUS_LABELS[order.status]}
          </span>
          {!isTerminal && (
            <p className="mt-2 text-sm text-gray-500 animate-pulse">自動更新中...</p>
          )}
        </div>

        <div className="border-t border-gray-200 pt-6 space-y-3 text-sm">
          <div className="flex justify-between">
            <span className="text-gray-500">訂單編號</span>
            <span className="font-mono text-xs text-gray-700 break-all text-right max-w-[60%]">{order.id}</span>
          </div>
          <div className="flex justify-between">
            <span className="text-gray-500">購買數量</span>
            <span className="text-gray-900">{order.quantity} 張</span>
          </div>
          <div className="flex justify-between">
            <span className="text-gray-500">訂單金額</span>
            <span className="font-semibold text-gray-900">NT$ {order.totalAmount.toLocaleString()}</span>
          </div>
          <div className="flex justify-between">
            <span className="text-gray-500">建立時間</span>
            <span className="text-gray-700">
              {new Date(order.createdAt).toLocaleString('zh-TW')}
            </span>
          </div>
        </div>

        {order.status === 'Success' && (
          <div className="bg-green-50 border border-green-200 rounded-lg p-4 text-center">
            <p className="text-green-700 font-semibold">🎉 恭喜！購票成功</p>
            <p className="text-green-600 text-sm mt-1">票券將由主辦單位寄發至您的 email</p>
          </div>
        )}

        {order.status === 'Failed' && (
          <div className="bg-red-50 border border-red-200 rounded-lg p-4 text-center">
            <p className="text-red-700 font-semibold">購票失敗</p>
            <p className="text-red-600 text-sm mt-1">庫存不足或處理過程發生錯誤，款項不會被扣除</p>
            <button
              onClick={() => router.push('/')}
              className="mt-3 text-blue-600 hover:underline text-sm"
            >
              返回重新搶票
            </button>
          </div>
        )}
      </div>
    </div>
  );
}
