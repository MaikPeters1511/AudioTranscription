import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AdminUserService } from './admin-user.service';

describe('AdminUserService', () => {
  let service: AdminUserService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(AdminUserService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists users with paging parameters', async () => {
    const result = service.list(2, 50);
    const req = http.expectOne((r) => r.url === '/api/admin/users');
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('pageSize')).toBe('50');
    req.flush({ items: [], total: 0 });

    expect(await result).toEqual({ items: [], total: 0 });
  });

  it('creates a user', async () => {
    const body = { email: 'a@example.com', password: 'x', roles: [] };
    const result = service.create(body);
    const req = http.expectOne('/api/admin/users');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush({ id: '1', email: 'a@example.com', roles: [], isActive: true });

    expect((await result).id).toBe('1');
  });

  it('sets roles, deactivates and activates by id', async () => {
    const roles = service.setRoles('a/b', ['Admin']);
    const rolesReq = http.expectOne('/api/admin/users/a%2Fb/roles');
    expect(rolesReq.request.method).toBe('PUT');
    expect(rolesReq.request.body).toEqual({ roles: ['Admin'] });
    rolesReq.flush(null, { status: 204, statusText: 'No Content' });
    await roles;

    const off = service.deactivate('7');
    http.expectOne('/api/admin/users/7/deactivate').flush(null, { status: 204, statusText: 'No Content' });
    await off;

    const on = service.activate('7');
    http.expectOne('/api/admin/users/7/activate').flush(null, { status: 204, statusText: 'No Content' });
    await on;
  });
});
