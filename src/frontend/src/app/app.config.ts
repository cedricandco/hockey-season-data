import { ApplicationConfig, provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient, withFetch } from '@angular/common/http';
import { provideIonicAngular } from '@ionic/angular';
import { provideTranslateService } from '@ngx-translate/core';
import { provideTranslateHttpLoader } from '@ngx-translate/http-loader';

/**
 * Root application configuration (zoneless Angular 22 + Ionic 9 + ngx-translate 18).
 */
export const appConfig: ApplicationConfig = {
  providers: [
    provideZonelessChangeDetection(),
    provideIonicAngular({ mode: 'md' }),
    provideHttpClient(withFetch()),
    // i18n: translations live in src/assets/i18n/{{lang}}.json, loaded over HTTP.
    provideTranslateService({
      lang: 'fr',
      fallbackLang: 'en',
      langs: ['fr', 'en'],
    }),
    provideTranslateHttpLoader({
      prefix: '/assets/i18n/',
      suffix: '.json',
    }),
  ],
};
