import { Routes } from '@angular/router';
import { PulsePageComponent } from './pulse/pulse-page.component';

/** One route. The dashboard's whole state is expressed in this route's query parameters,
 *  which is what makes a reload and a shared link land on the same view. */
export const routes: Routes = [
  { path: '', component: PulsePageComponent },
  { path: '**', redirectTo: '' },
];
