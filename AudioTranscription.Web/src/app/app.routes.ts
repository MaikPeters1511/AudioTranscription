import { Routes } from '@angular/router';
import { authGuard } from './auth/auth.guard';
import { adminGuard } from './auth/admin.guard';

// Pages are lazy-loaded to keep the initial bundle small
export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./auth/login.component').then((m) => m.LoginComponent),
  },
  { path: '', redirectTo: 'upload', pathMatch: 'full' },
  {
    path: 'upload',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./components/upload/upload.component').then((m) => m.UploadComponent),
  },
  {
    path: 'jobs',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./components/job-list/job-list.component').then((m) => m.JobListComponent),
  },
  {
    path: 'search',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./components/search/search.component').then((m) => m.SearchComponent),
  },
  {
    path: 'jobs/:id',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./components/job-detail/job-detail.component').then((m) => m.JobDetailComponent),
  },
  {
    path: 'organize',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./components/organize/organize.component').then((m) => m.OrganizeComponent),
  },
  {
    path: 'glossaries',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./components/glossary/glossary.component').then((m) => m.GlossaryComponent),
  },
  {
    path: 'account',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./auth/change-password.component').then((m) => m.ChangePasswordComponent),
  },
  {
    path: 'admin/users',
    canActivate: [authGuard, adminGuard],
    loadComponent: () =>
      import('./components/user-admin/user-admin.component').then((m) => m.UserAdminComponent),
  },
  { path: '**', redirectTo: 'upload' },
];
