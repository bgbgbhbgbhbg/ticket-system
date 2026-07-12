'use client';

import { useState, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { apiClient, type Ticket } from '@/app/lib/api';

interface TicketDetailProps {
  ticketId: string;
}

export default function TicketDetail({ ticketId }: TicketDetailProps) {
  const router = useRouter();
  const [ticket, setTicket] = useState<Ticket | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

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
          <button
            disabled
            className="w-full bg-gray-300 text-gray-500 py-3 px-6 rounded-lg font-semibold cursor-not-allowed"
          >
            登入後即可購票（Task 3 實作中）
          </button>
          <p className="mt-2 text-sm text-gray-500 text-center">
            * 購票功能將在 Task 3 (Auth) 和 Task 4 (Orders) 完成後啟用
          </p>
        </div>
      </div>
    </div>
  );
}
