import { Component, inject } from '@angular/core';
import { HomeComponent } from './home/home.component';
import { LoginComponent } from './core/login.component';
import { AuthService } from './core/auth.service';

/**
 * Application root: shows the login gate until the user is authenticated,
 * then hosts the main app surface. Authentication state is reactive, so a
 * 401 anywhere in the app (token expired/revoked) flips back to the login
 * screen automatically.
 */
@Component({
  selector: 'app-root',
  standalone: true,
  imports: [HomeComponent, LoginComponent],
  template: `
    @if (auth.isLoggedIn) {
      <app-home />
    } @else {
      <app-login />
    }
  `,
})
export class AppComponent {
  readonly auth = inject(AuthService);
}
