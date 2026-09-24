import { Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-[name]',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="card bg-base-100 shadow-xl">
      <div class="card-body">
        <h2 class="card-title">{{ title() }}</h2>
        <p>Content goes here</p>
        <div class="card-actions justify-end">
          <button class="btn btn-primary" (click)="onClick()">Action</button>
        </div>
      </div>
    </div>
  `
})
export class [Name]Component {
  title = signal('[Name] Component');

  onClick() {
    console.log('Action triggered');
  }
}
