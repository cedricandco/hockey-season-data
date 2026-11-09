import { Component } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';

/**
 * Weekly report page. Deliberately empty for now; the placeholder marks
 * where the future report will appear.
 */
@Component({
  selector: 'app-report',
  standalone: true,
  imports: [TranslatePipe],
  template: `
    <section class="page-container" aria-labelledby="report-title">
      <h1 id="report-title" class="page-title">{{ 'REPORT.TITLE' | translate }}</h1>
      <p class="page-subtitle">{{ 'REPORT.SUBTITLE' | translate }}</p>
      <div class="empty-state" role="status">
        <p>{{ 'REPORT.EMPTY' | translate }}</p>
      </div>
    </section>
  `,
})
export class ReportComponent {}
