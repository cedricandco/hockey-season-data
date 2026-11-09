import { Routes } from '@angular/router';
import { ChatComponent } from './chat/chat.component';
import { ReportComponent } from './report/report.component';
import { ConfigurationComponent } from './config/config.component';

/** Tabs of the authenticated app shell. */
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'chat' },
  { path: 'chat', component: ChatComponent },
  { path: 'teams-weekly-report', component: ReportComponent },
  { path: 'rapport', pathMatch: 'full', redirectTo: 'teams-weekly-report' },
  { path: 'configuration', component: ConfigurationComponent },
  { path: '**', redirectTo: 'chat' },
];
