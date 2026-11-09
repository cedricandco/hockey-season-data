import { Component } from '@angular/core';
import { IonApp } from '@ionic/angular';
import { HomeComponent } from './home/home.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [IonApp, HomeComponent],
  template: `
    <ion-app>
      <app-home />
    </ion-app>
  `,
})
export class AppComponent {}
