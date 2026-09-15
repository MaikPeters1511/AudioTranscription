import { Injectable, inject, signal } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { AudioJobListItem, AudioJobStatus } from '../models/audio-job.model';
import { AudioJobService } from './audio-job.service';

@Injectable({ providedIn: 'root' })
export class SignalRService {
  private hubConnection?: signalR.HubConnection;
  private audioJobService = inject(AudioJobService);
  readonly connected = signal(false);

  start(baseUrl: string): void {
    // Construct hub URL from API base
    const hubUrl = `${baseUrl}/hubs/transcription`;

    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl)
      .withAutomaticReconnect()
      .build();

    this.hubConnection.onreconnecting(() => this.connected.set(false));
    this.hubConnection.onreconnected(() => this.connected.set(true));
    this.hubConnection.onclose(() => this.connected.set(false));

    // Listen for job status updates
    this.hubConnection.on('JobStatusChanged', (job: AudioJobListItem) => {
      this.audioJobService.updateJobInList(job);
      this.audioJobService.updateSelectedJobFromListItem(job);

      // If the currently open job just finished, silently refetch it to pick up
      // the transcript text / error message, which isn't part of the list payload.
      const selected = this.audioJobService.selectedJob();
      if (selected?.id === job.id && (job.status === AudioJobStatus.Completed || job.status === AudioJobStatus.Failed)) {
        this.audioJobService.loadJob(job.id, true);
      }
    });

    this.hubConnection.on('JobCreated', (job: AudioJobListItem) => {
      this.audioJobService.updateJobInList(job);
    });

    this.hubConnection
      .start()
      .then(() => {
        this.connected.set(true);
        console.log('SignalR connected');
      })
      .catch((err) => {
        console.warn('SignalR connection failed, will use polling:', err);
        this.connected.set(false);
      });
  }

  stop(): void {
    this.hubConnection?.stop();
    this.connected.set(false);
  }
}
