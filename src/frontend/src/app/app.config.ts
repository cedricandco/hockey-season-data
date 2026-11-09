import { ApplicationConfig, provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient, withFetch } from '@angular/common/http';
import { provideIonicAngular } from '@ionic/angular';
import { provideTranslateService } from '@ngx-translate/core';
import { provideTranslateHttpLoader } from '@ngx-translate/http-loader';
import { HttpAgent } from '@ag-ui/client';
import { provideCopilotChatConfiguration, provideCopilotKit } from '@copilotkit/angular';
import { environment } from '../environments/environment';

/** Logical id of the translation agent exposed by the backend over AG-UI. */
export const HOCKEY_SEASON_DATA_AGENT_ID = 'hockey-season-data.agent';

/**
 * Self-managed AG-UI agent talking straight to the .NET backend's
 * `MapAGUIServer` endpoint. The relative URL keeps same-origin calls working
 * in dev (proxy.conf.cjs → http://localhost:5000) and with the Azure Static
 * Web Apps managed Functions API.
 */
export const hockeySeasonDataAgent = new HttpAgent({
  agentId: HOCKEY_SEASON_DATA_AGENT_ID,
  description: 'Translates the user input message into English.',
  url: `${environment.apiUrl}/agui/hockey-season-data.agent`,
});

/**
 * Root application configuration (zoneless Angular 22 + Ionic 9 + ngx-translate 18
 * + CopilotKit Angular UI connected to the Microsoft Agent Framework backend).
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
    // CopilotKit UI wired to the backend AG-UI agent (no CopilotKit runtime server needed).
    provideCopilotKit({
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
