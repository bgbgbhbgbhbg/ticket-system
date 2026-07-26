'use client';

import { useState, useEffect, useCallback } from 'react';
import { useRouter } from 'next/navigation';
import { apiClient, type Order, type ApiError } from '@/app/lib/api';
import { useAuth } from '@/app/lib/auth-context';

const STATUS_OPTIONS = ['', 'Pending', 'Processing', 'Success', 'Failed'] as const;
type StatusFilter = typeof STATUS_OPTIONS[number];

const STATUS_LABELS: Record<string, string> = {
  Pending: '等待處理',
  Processing: '處理中',
  Success: '購票成功',
  Failed: '購票失敗',
};

const STATUS_COLORS: Record<string, string> = {
  Pending: 'text-yellow-700 bg-yellow-50 border-yellow-200',
  Processing: 'text-blue-700 bg-blue-50 border-blue-200',
  Success: 'text-green-700 bg-green-50 border-green-200',
  Failed: 'text-red-700 bg-red-50 border-red-200',
};

export default function AdminOrdersPage() {
  const router = useRouter();
  const { user, token } = useAuth();

  const [orders, setOrders] = useState<Order[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const pageSize = 20;
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // 更新狀態 modal
  const [updating, setUpdating] = useState<Order | null>(null);
  const [toStatus, setToStatus] = useState<'Success' | 'Failed'>('Failed');
  const [reason, setReason] = useState('');
  const [updateError, setUpdateError] = useState<string | null>(null);
  const [updateLoading, setUpdateLoading] = useState(false);

  const fetchOrders = useCallback(async () => {
    if (!token) return;
    setLoading(true);
    try {
      const data = await apiClient.adminGetOrders(token, {
        status: statusFilter || undefined,
        page,
        pageSize,
      });
      setOrders(data.items);
      setTotal(data.total);
      setError(null);
    } catch (err) {
      const apiErr = err as ApiError;
      setError(apiErr?.message ?? '載入失敗');
    } finally {
      setLoading(false);
    }
  }, [token, statusFilter, page]);

  useEffect(() => {
    fetchOrders();
  }, [fetchOrders]);

  // 非 Admin 導回首頁
  useEffect(() => {
    if (user && user.role !== 'Admin') {
      router.replace('/');
    }
  }, [user, router]);

  const handleUpdateStatus = async () => {
    if (!token || !updating) return;
    if (!reason.trim()) {
      setUpdateError('請填寫介入原因');
      return;
    }
    setUpdateLoading(true);
    setUpdateError(null);
    try {
      await apiClient.adminUpdateOrderStatus(token, updating.id, toStatus, reason);
      setUpdating(null);
      setReason('');
      fetchOrders();
    } catch (err) {
      const apiErr = err as ApiError;
      if (apiErr?.errorCode === 'ORDER_INVALID_STATUS_TRANSITION') {
        setUpdateError('此狀態不允許轉換（終態不可逆或轉換不合法）');
      } else {
        setUpdateError(apiErr?.message ?? '更新失敗');
      }
    } finally {
      setUpdateLoading(false);
    }
  };

  if (!token || (user && user.role !== 'Admin')) {
    return (
      <div className="min-h-screen flex items-center justify-center">
        <p className="text-gray-500">存取受限</p>
      </div>
    );
  }

  const totalPages = Math.ceil(total / pageSize);

  return (
    <div className="min-h-screen bg-gray-50">
      <header className="bg-white shadow">
        <div className="max-w-7xl mx-auto py-6 px-4 sm:px-6 lg:px-8 flex items-center justify-between">
          <div>
            <h1 className="text-2xl font-bold text-gray-900">訂單管理後台</h1>
            <p className="text-sm text-gray-500 mt-1">Admin 專用 — 可查看所有使用者訂單並手動介入狀態</p>
          </div>
          <button
            onClick={() => router.push('/')}
            className="text-blue-600 hover:underline text-sm"
          >
            ← 返回前台
          </button>
        </div>
      </header>

      <main className="max-w-7xl mx-auto py-6 px-4 sm:px-6 lg:px-8 space-y-4">
        {/* 篩選列 */}
        <div className="flex items-center gap-4 bg-white rounded-lg border border-gray-200 p-4">
          <label className="text-sm font-medium text-gray-700">狀態篩選</label>
          <select
            value={statusFilter}
            onChange={(e) => { setStatusFilter(e.target.value as StatusFilter); setPage(1); }}
            className="border border-gray-300 rounded px-3 py-1.5 text-sm focus:ring-blue-500 focus:border-blue-500"
          >
            <option value="">全部</option>
            {STATUS_OPTIONS.filter(Boolean).map((s) => (
              <option key={s} value={s}>{STATUS_LABELS[s]}</option>
            ))}
          </select>
          <span className="ml-auto text-sm text-gray-500">共 {total} 筆</span>
        </div>

        {/* 訂單列表 */}
        {loading ? (
          <div className="flex justify-center py-12">
            <div className="animate-spin rounded-full h-10 w-10 border-b-2 border-blue-600" />
          </div>
        ) : error ? (
          <div className="bg-red-50 border border-red-200 rounded-lg p-4 text-red-600">{error}</div>
        ) : orders.length === 0 ? (
          <div className="bg-white border border-gray-200 rounded-lg p-8 text-center text-gray-500">
            目前沒有訂單
          </div>
        ) : (
          <div className="bg-white border border-gray-200 rounded-lg overflow-hidden">
            <table className="min-w-full divide-y divide-gray-200">
              <thead className="bg-gray-50">
                <tr>
                  {['訂單編號', '票券', '數量', '金額', '狀態', '建立時間', '操作'].map((h) => (
                    <th key={h} className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                      {h}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {orders.map((order) => (
                  <tr key={order.id} className="hover:bg-gray-50">
                    <td className="px-4 py-3 font-mono text-xs text-gray-600 max-w-[120px] truncate">{order.id}</td>
                    <td className="px-4 py-3 font-mono text-xs text-gray-600 max-w-[120px] truncate">{order.ticketId}</td>
                    <td className="px-4 py-3 text-sm text-gray-900">{order.quantity}</td>
                    <td className="px-4 py-3 text-sm font-medium text-gray-900">NT$ {order.totalAmount.toLocaleString()}</td>
                    <td className="px-4 py-3">
                      <span className={`inline-block px-2 py-0.5 rounded-full border text-xs font-semibold ${STATUS_COLORS[order.status] ?? ''}`}>
                        {STATUS_LABELS[order.status] ?? order.status}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-xs text-gray-500">
                      {new Date(order.createdAt).toLocaleString('zh-TW')}
                    </td>
                    <td className="px-4 py-3">
                      {/* 只有非終態才能手動介入 */}
                      {(order.status === 'Pending' || order.status === 'Processing') && (
                        <button
                          onClick={() => { setUpdating(order); setToStatus('Failed'); setReason(''); setUpdateError(null); }}
                          className="text-xs text-blue-600 hover:underline"
                        >
                          手動介入
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {/* 分頁 */}
        {totalPages > 1 && (
          <div className="flex items-center justify-center gap-2">
            <button
              disabled={page === 1}
              onClick={() => setPage((p) => p - 1)}
              className="px-3 py-1 text-sm border border-gray-300 rounded disabled:opacity-40 hover:bg-gray-100"
            >
              上一頁
            </button>
            <span className="text-sm text-gray-600">{page} / {totalPages}</span>
            <button
              disabled={page >= totalPages}
              onClick={() => setPage((p) => p + 1)}
              className="px-3 py-1 text-sm border border-gray-300 rounded disabled:opacity-40 hover:bg-gray-100"
            >
              下一頁
            </button>
          </div>
        )}
      </main>

      {/* 手動介入 Modal */}
      {updating && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
          <div className="bg-white rounded-lg shadow-xl p-6 w-full max-w-md mx-4 space-y-4">
            <h2 className="text-lg font-bold text-gray-900">手動介入訂單狀態</h2>
            <p className="text-xs text-gray-500 font-mono break-all">{updating.id}</p>

            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">目標狀態</label>
              <select
                value={toStatus}
                onChange={(e) => setToStatus(e.target.value as 'Success' | 'Failed')}
                className="w-full border border-gray-300 rounded px-3 py-2 text-sm"
              >
                <option value="Success">購票成功 (Success)</option>
                <option value="Failed">購票失敗 (Failed)</option>
              </select>
            </div>

            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">介入原因 <span className="text-red-500">*</span></label>
              <textarea
                value={reason}
                onChange={(e) => setReason(e.target.value)}
                rows={3}
                maxLength={480}
                placeholder="請說明手動介入的原因..."
                className="w-full border border-gray-300 rounded px-3 py-2 text-sm resize-none focus:ring-blue-500 focus:border-blue-500"
              />
            </div>

            {updateError && (
              <p className="text-sm text-red-600 bg-red-50 rounded p-2">{updateError}</p>
            )}

            <div className="flex gap-3 justify-end">
              <button
                onClick={() => setUpdating(null)}
                disabled={updateLoading}
                className="px-4 py-2 text-sm border border-gray-300 rounded hover:bg-gray-100 disabled:opacity-40"
              >
                取消
              </button>
              <button
                onClick={handleUpdateStatus}
                disabled={updateLoading}
                className="px-4 py-2 text-sm bg-blue-600 text-white rounded hover:bg-blue-700 disabled:opacity-40"
              >
                {updateLoading ? '更新中...' : '確認更新'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
