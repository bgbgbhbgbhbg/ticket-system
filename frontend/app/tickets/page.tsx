'use client';

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';

export default function TicketsPage() {
  const router = useRouter();

  useEffect(() => {
    // 重定向回主頁（主頁已顯示 tickets 清單）
    router.push('/');
  }, [router]);

  return null;
}
