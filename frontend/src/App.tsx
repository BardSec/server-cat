import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ServerCatalog } from './pages/ServerCatalog';
import { ServerDetail } from './pages/ServerDetail';
import { AddEditServer } from './pages/AddEditServer';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      retry: 1,
    },
  },
});

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <Routes>
          <Route path="/" element={<ServerCatalog />} />
          <Route path="/servers/new" element={<AddEditServer />} />
          <Route path="/servers/:id" element={<ServerDetail />} />
          <Route path="/servers/:id/edit" element={<AddEditServer />} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </BrowserRouter>
    </QueryClientProvider>
  );
}
