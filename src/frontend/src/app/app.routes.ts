import { Routes } from '@angular/router';
import { ChatComponent } from './chat/chat.component';
import { ReportComponent } from './report/report.component';
import { ConfigurationComponent } from './config/config.component';

/** Tabs of the authenticated app shell. */
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'chat' },
  { path: 'chat', component: ChatComponent },
  { path: 'rapport', component: ReportComponent },
  { path: 'configuration', component: ConfigurationComponent },
  { path: '**', redirectTo: 'chat' },
];
