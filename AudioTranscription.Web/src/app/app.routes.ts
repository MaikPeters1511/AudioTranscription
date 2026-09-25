import { Routes } from '@angular/router';
import { authGuard } from './auth/auth.guard';

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
  { path: '**', redirectTo: 'upload' },
];
