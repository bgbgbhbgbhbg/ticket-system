'use client';

import TicketList from './components/TicketList';
import Link from 'next/link';
import { useAuth } from './lib/auth-context';

export default function Home() {
  const { user, logout } = useAuth();

  return (
    <div className="min-h-screen bg-gray-50">
      <header className="bg-white shadow">
        <div className="max-w-7xl mx-auto py-6 px-4 sm:px-6 lg:px-8">
          <div className="flex justify-between items-center">
            <div>
              <h1 className="text-3xl font-bold text-gray-900">
                高併發票券預訂系統
              </h1>
              <p className="mt-2 text-sm text-gray-600">
                Ticket Booking System
              </p>
            </div>
            <div className="flex items-center space-x-4">
              {user ? (
                <>
                  <span className="text-sm text-gray-700">
                    歡迎, {user.displayName}
                  </span>
                  <button
                    onClick={logout}
                    className="bg-gray-200 hover:bg-gray-300 text-gray-800 font-medium py-2 px-4 rounded"
                  >
                    登出
                  </button>
                </>
              ) : (
                <>
                  <Link
                    href="/auth/login"
                    className="text-blue-600 hover:text-blue-800 font-medium"
                  >
                    登入
                  </Link>
                  <Link
                    href="/auth/register"
                    className="bg-blue-600 hover:bg-blue-700 text-white font-medium py-2 px-4 rounded"
                  >
                    註冊
                  </Link>
                </>
              )}
            </div>
          </div>
        </div>
      </header>
      <main className="max-w-7xl mx-auto py-6 sm:px-6 lg:px-8">
        <div className="px-4 py-6 sm:px-0">
          <TicketList />
        </div>
      </main>
    </div>
  );
}
