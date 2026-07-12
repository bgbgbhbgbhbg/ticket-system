'use client';

import { useState, useEffect } from 'react';
import Link from 'next/link';
import { apiClient, type Ticket } from '@/app/lib/api';

export default function TicketList() {
  const [tickets, setTickets] = useState<Ticket[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    async function loadTickets() {
      try {
        setLoading(true);
        const data = await apiClient.getAllTickets();
        setTickets(data);
        setError(null);
      } catch (err) {
        setError(err instanceof Error ? err.message : '載入失敗');
        console.error('Failed to load tickets:', err);
      } finally {
        setLoading(false);
      }
    }

    loadTickets();
  }, []);

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
      </div>
    );
  }

  if (tickets.length === 0) {
    return (
      <div className="bg-gray-50 border border-gray-200 rounded-lg p-8 text-center">
        <p className="text-gray-600">目前沒有可預訂的票券</p>
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <h2 className="text-2xl font-bold text-gray-900">熱門票券</h2>
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
        {tickets.map((ticket) => (
          <Link
            key={ticket.id}
            href={`/tickets/${ticket.id}`}
            className="block bg-white border border-gray-200 rounded-lg p-6 hover:shadow-lg transition-shadow"
          >
            <div className="space-y-3">
              <h3 className="text-xl font-semibold text-gray-900">
                {ticket.eventName}
              </h3>
              <p className="text-gray-600">{ticket.name}</p>
              <div className="flex items-center justify-between">
                <p className="text-2xl font-bold text-blue-600">
                  NT$ {ticket.price.toLocaleString()}
                </p>
                <p className="text-sm text-gray-500">
                  剩餘 {ticket.availableQuantity} 張
                </p>
              </div>
              <p className="text-sm text-gray-500">
                活動時間: {new Date(ticket.eventStartAt).toLocaleString('zh-TW', {
                  year: 'numeric',
                  month: '2-digit',
                  day: '2-digit',
                  hour: '2-digit',
                  minute: '2-digit',
                })}
              </p>
            </div>
          </Link>
        ))}
      </div>
    </div>
  );
}
