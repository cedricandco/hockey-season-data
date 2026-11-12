import { ApplicationConfig, ENVIRONMENT_INITIALIZER, inject, Injector, provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { authHeaderMirrorInterceptor } from './core/auth-header.interceptor';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { provideTranslateHttpLoader } from '@ngx-translate/http-loader';
import { provideCopilotChatConfiguration, provideCopilotKit } from '@copilotkit/angular';
import { provideAuth0, authHttpInterceptorFn } from '@auth0/auth0-angular';
import { environment } from '../environments/environment';
import { HockeyAssistantHttpAgent } from './agent/hockey-assistant-http-agent';
import { AuthService } from '@auth0/auth0-angular';
import { firstValueFrom } from 'rxjs';
import { routes } from './app.routes';

/** Logical id of the Hockey Assistant agent exposed by the backend over AG-UI. */
export const HOCKEY_SEASON_DATA_AGENT_ID = 'hockey-season-data.agent';

/**
 * Self-managed AG-UI agent talking straight to the .NET backend's
 * AG-UI Function endpoint. The relative URL keeps same-origin calls working
 * in dev (proxy.conf.cjs → http://localhost:7071) and with the Azure Static
 * Web Apps managed Functions API.
 */
export const hockeySeasonDataAgent = new HockeyAssistantHttpAgent({
  agentId: HOCKEY_SEASON_DATA_AGENT_ID,
  description: 'Answers questions about hockey leagues, teams, games, standings and suspensions.',
  url: `${environment.apiUrl}/agui/hockey-season-data.agent`,
});

// Auth0 token acquisition and renewal are asynchronous, including cache hits.
HockeyAssistantHttpAgent.accessTokenProvider = () => {
  if (!globalInjector) throw new Error('The application injector is not initialized.');
  return firstValueFrom(globalInjector.get(AuthService).getAccessTokenSilently());
};

/** Root injector captured at boot so lazy providers resolve AuthService. */
let globalInjector: Injector | null = null;

/**
 * Root application configuration (zoneless Angular 22 + ngx-translate 18
 * + CopilotKit Angular UI connected to the Microsoft Agent Framework backend).
 */
export const appConfig: ApplicationConfig = {
  providers: [
    provideZonelessChangeDetection(),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withFetch(), withInterceptors([authHttpInterceptorFn, authHeaderMirrorInterceptor])),
    // Auth0 Universal Login (Authorization Code + PKCE): the SDK owns token
    // storage and renewal; business API calls receive the bearer token via
    // authHttpInterceptorFn, agent runs via HockeyAssistantHttpAgent.
    provideAuth0({
      domain: environment.auth0.domain,
      clientId: environment.auth0.clientId,
      authorizationParams: {
        redirect_uri: typeof window !== 'undefined' ? window.location.origin : undefined,
        audience: environment.auth0.audience,
        scope: 'openid profile email read:reports read:teams use:agent',
      },
      httpInterceptor: {
        // Only API calls get the Authorization header (never the i18n assets
        // or the AG-UI agent, which manages its own token).
        allowedList: [{ uriMatcher: (uri) => uri.startsWith('/api/') && !uri.startsWith('/api/agui/') }],
      },
    }),
    {
      provide: ENVIRONMENT_INITIALIZER,
      multi: true,
      useValue: () => {
        globalInjector = inject(Injector);
      },
    },
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
    // CopilotKit UI wired to the backend AG-UI agent (no CopilotKit runtime server needed).
    provideCopilotKit({
      enableInspector: false,
      selfManagedAgents: {
        [HOCKEY_SEASON_DATA_AGENT_ID]: hockeySeasonDataAgent,
      },
    }),
    // Prebuilt surfaces (copilot-sidebar / copilot-popup) render their own chat
    // component and do not project content, so they resolve the agent from this
    // ambient configuration instead of CopilotKit's "default" agent (which does
    // not exist here).
    provideCopilotChatConfiguration({ agentId: HOCKEY_SEASON_DATA_AGENT_ID }),
  ],
};