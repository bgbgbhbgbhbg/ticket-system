import ApiTest from './components/ApiTest';

export default function Home() {
  return (
    <div className="flex flex-col flex-1 items-center justify-center bg-zinc-50 font-sans dark:bg-black min-h-screen">
      <main className="flex flex-col w-full max-w-4xl items-center justify-start py-12 px-6 bg-white dark:bg-black sm:items-center">
        <div className="mb-8 text-center">
          <h1 className="text-4xl font-bold leading-10 tracking-tight text-black dark:text-zinc-50 mb-2">
            Ticket Booking System
          </h1>
          <p className="text-lg text-zinc-600 dark:text-zinc-400">
            Frontend → Backend API Integration Test
          </p>
        </div>

        <div className="w-full">
          <ApiTest />
        </div>

        <div className="mt-8 p-4 bg-blue-50 dark:bg-blue-900/20 border border-blue-200 dark:border-blue-800 rounded-lg text-blue-900 dark:text-blue-300 text-sm w-full">
          <p className="font-semibold mb-2">📋 Development Setup Status:</p>
          <ul className="list-disc list-inside space-y-1">
            <li>Frontend: Next.js on localhost:3000 ✅</li>
            <li>Backend: ASP.NET Core on localhost:5263</li>
            <li>API Docs: http://localhost:5263/scalar/v1</li>
            <li>CORS: Configured to allow frontend requests</li>
          </ul>
        </div>
      </main>
    </div>
  );
}
