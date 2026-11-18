import { Routes } from '@angular/router';
import { ChatComponent } from './chat/chat.component';
import { ReportComponent } from './report/report.component';
import { ConfigurationComponent } from './config/config.component';
import { TeamComponent } from './team/team.component';
import { AdminComponent } from './admin/admin.component';
import { AdminReferenceComponent } from './admin/admin-reference.component';
import { adminGuard } from './core/admin.guard';

/** Tabs of the authenticated app shell. */
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'chat' },
  { path: 'chat', component: ChatComponent },
  { path: 'team', component: TeamComponent },
  { path: 'teams-weekly-report', component: ReportComponent },
  { path: 'rapport', pathMatch: 'full', redirectTo: 'teams-weekly-report' },
  { path: 'configuration', component: ConfigurationComponent },
  // Administrator only: the guard checks the caller's local account flag.
  { path: 'admin', component: AdminComponent, canActivate: [adminGuard] },
  // Administrator only: divisions / leagues / teams import from Spordle.
  { path: 'admin/reference', component: AdminReferenceComponent, canActivate: [adminGuard] },
  { path: '**', redirectTo: 'chat' },
];
