import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

export const ADMIN_ROLE = 'Admin';

export interface AdminUser {
  id: string;
  email: string;
  roles: string[];
  isActive: boolean;
}

export interface AdminUserPage {
  items: AdminUser[];
  total: number;
}

export interface CreateUserRequest {
  email: string;
  password: string;
  roles: string[];
}

/** User administration endpoints (S18); the API only allows them for the Admin role. */
@Injectable({ providedIn: 'root' })
export class AdminUserService {
  private http = inject(HttpClient);
  private readonly baseUrl = '/api/admin/users';

  list(page = 1, pageSize = 100): Promise<AdminUserPage> {
    return firstValueFrom(
      this.http.get<AdminUserPage>(this.baseUrl, { params: { page, pageSize } }),
    );
  }

  create(request: CreateUserRequest): Promise<AdminUser> {
    return firstValueFrom(this.http.post<AdminUser>(this.baseUrl, request));
  }

  setRoles(id: string, roles: string[]): Promise<unknown> {
    return firstValueFrom(
      this.http.put(`${this.baseUrl}/${encodeURIComponent(id)}/roles`, { roles }),
    );
  }

  deactivate(id: string): Promise<unknown> {
    return firstValueFrom(this.http.post(`${this.baseUrl}/${encodeURIComponent(id)}/deactivate`, null));
  }

  activate(id: string): Promise<unknown> {
    return firstValueFrom(this.http.post(`${this.baseUrl}/${encodeURIComponent(id)}/activate`, null));
  }
}
