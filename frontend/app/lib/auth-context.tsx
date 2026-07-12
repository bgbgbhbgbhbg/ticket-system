'use client';

import React, { createContext, useContext, useState, useEffect } from 'react';

interface User {
  id: string;
  email: string;
  displayName: string;
  role: string;
  createdAt: string;
}

interface AuthContextType {
  user: User | null;
  token: string | null;
  login: (email: string, password: string) => Promise<void>;
  register: (email: string, password: string, displayName: string) => Promise<void>;
  logout: () => void;
  isLoading: boolean;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

const API_BASE_URL = process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5000/api/v1';

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [token, setToken] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  // 從 localStorage 載入 token 並驗證
  useEffect(() => {
    const loadUser = async () => {
      const storedToken = localStorage.getItem('auth_token');
      if (storedToken) {
        try {
          const response = await fetch(`${API_BASE_URL}/auth/me`, {
            headers: {
              Authorization: `Bearer ${storedToken}`,
            },
          });

          if (response.ok) {
            const userData = await response.json();
            setUser(userData);
            setToken(storedToken);
          } else {
            // Token 無效，清除
            localStorage.removeItem('auth_token');
          }
        } catch (error) {
          console.error('Failed to load user:', error);
          localStorage.removeItem('auth_token');
        }
      }
      setIsLoading(false);
    };

    loadUser();
  }, []);

  const login = async (email: string, password: string) => {
    const response = await fetch(`${API_BASE_URL}/auth/login`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ email, password }),
    });

    if (!response.ok) {
      const error = await response.json();
      throw new Error(error.message || 'Login failed');
    }

    const data = await response.json();
    const newToken = data.accessToken;

    // 取得使用者資訊
    const userResponse = await fetch(`${API_BASE_URL}/auth/me`, {
      headers: {
        Authorization: `Bearer ${newToken}`,
      },
    });

    if (!userResponse.ok) {
      throw new Error('Failed to fetch user info');
    }

    const userData = await userResponse.json();

    setToken(newToken);
    setUser(userData);
    localStorage.setItem('auth_token', newToken);
  };

  const register = async (email: string, password: string, displayName: string) => {
    const response = await fetch(`${API_BASE_URL}/auth/register`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ email, password, displayName }),
    });

    if (!response.ok) {
      const error = await response.json();
      throw new Error(error.message || 'Registration failed');
    }

    const userData = await response.json();

    // 註冊成功後自動登入
    await login(email, password);
  };

  const logout = () => {
    setUser(null);
    setToken(null);
    localStorage.removeItem('auth_token');
  };

  return (
    <AuthContext.Provider value={{ user, token, login, register, logout, isLoading }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (context === undefined) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
}
