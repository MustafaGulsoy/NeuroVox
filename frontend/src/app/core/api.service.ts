import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import {
  Ace, AnalysisInfo, Annotation, AppUser, KaggleOverview, TrainingRun, GroupComparison, RaterAgreement, VisitPrediction, FeatureDefinition, Outcome, Participant, Protocol, Recording, Role, Stimulus, Summary, Visit
} from './models';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private http = inject(HttpClient);

  summary() { return this.http.get<Summary>('/api/Dashboard/summary'); }

  participants(skip = 0, take = 200) { return this.http.get<Participant[]>('/api/Participants', { params: { skip, take } }); }
  participant(id: string) { return this.http.get<Participant>(`/api/Participants/${id}`); }
  createParticipant(b: object) { return this.http.post<{ id: string }>('/api/Participants', b); }
  setConsent(id: string, given: boolean, consentVersion?: string) { return this.http.post<void>(`/api/Participants/${id}/consent`, { given, consentVersion }); }
  eraseParticipant(id: string) { return this.http.delete<void>(`/api/Participants/${id}`); }

  visits(participantId: string) { return this.http.get<Visit[]>(`/api/StudyVisits/by-participant/${participantId}`); }
  createVisit(b: object) { return this.http.post<{ id: string }>('/api/StudyVisits', b); }

  recordings(visitId?: string) {
    return this.http.get<Recording[]>('/api/SpeechRecordings', { params: visitId ? new HttpParams().set('visitId', visitId) : {} });
  }
  recording(id: string) { return this.http.get<Recording>(`/api/SpeechRecordings/${id}`); }
  upload(form: FormData) { return this.http.post<{ id: string }>('/api/SpeechRecordings/upload', form); }
  analyze(id: string) { return this.http.post<{ id: string; status: number }>(`/api/SpeechRecordings/${id}/analyze`, {}); }
  analysis(id: string) { return this.http.get<AnalysisInfo>(`/api/SpeechRecordings/${id}/analysis`); }
  deleteRecording(id: string) { return this.http.delete<void>(`/api/SpeechRecordings/${id}`); }
  audio(id: string) { return this.http.get(`/api/SpeechRecordings/${id}/audio`, { responseType: 'blob' }); }

  annotations(recordingId: string) { return this.http.get<Annotation[]>(`/api/TherapistAnnotations/by-recording/${recordingId}`); }
  createAnnotation(b: object) { return this.http.post<{ id: string }>('/api/TherapistAnnotations', b); }

  protocols() { return this.http.get<Protocol[]>('/api/ResearchProtocols'); }
  createProtocol(b: object) { return this.http.post<{ id: string }>('/api/ResearchProtocols', b); }
  stimuli(protocolId?: string) { return this.http.get<Stimulus[]>('/api/Stimuli', { params: protocolId ? new HttpParams().set('protocolId', protocolId) : {} }); }
  createStimulus(b: object) { return this.http.post<{ id: string }>('/api/Stimuli', b); }
  features() { return this.http.get<FeatureDefinition[]>('/api/FeatureDefinitions'); }

  aces(participantId: string) { return this.http.get<Ace[]>(`/api/AceAssessments/by-participant/${participantId}`); }
  createAce(b: object) { return this.http.post<{ id: string }>('/api/AceAssessments', b); }
  outcomes(participantId: string) { return this.http.get<Outcome[]>(`/api/ClinicalOutcomes/by-participant/${participantId}`); }
  createOutcome(b: object) { return this.http.post<{ id: string }>('/api/ClinicalOutcomes', b); }

  association(speechFeature: string, cognitiveScore: string) {
    return this.http.get<{ n: number; pearson: number | null; spearman: number | null }>('/api/Analysis/association', { params: { speechFeature, cognitiveScore } });
  }
  longitudinal(participantId: string, featureName: string) {
    return this.http.get<any>(`/api/Analysis/longitudinal/${participantId}`, { params: { featureName } });
  }
  setEligibility(id: string, b: object) { return this.http.put<{ eligible: boolean }>(`/api/Participants/${id}/eligibility`, b); }
  setEthics(id: string, b: object) { return this.http.put<void>(`/api/ResearchProtocols/${id}/ethics`, b); }
  groupComparison(by: string) { return this.http.get<GroupComparison>('/api/Analysis/group-comparison', { params: { by } }); }
  raterAgreement() { return this.http.get<RaterAgreement>('/api/Analysis/rater-agreement'); }
  predictVisit(visitId: string) { return this.http.get<VisitPrediction>(`/api/Predictions/visit/${visitId}`); }
  trainingCsv() { return this.http.get('/api/Export/training-set.csv', { responseType: 'blob' }); }

  kaggleAccounts() { return this.http.get<KaggleOverview>('/api/KaggleAccounts'); }
  trainingRuns() { return this.http.get<TrainingRun[]>('/api/Training'); }
  startTraining() { return this.http.post<{ id: string }>('/api/Training', {}); }
  addKaggleAccount(username: string, apiKey: string) { return this.http.post<{ id: string }>('/api/KaggleAccounts', { username, apiKey }); }
  connectKaggle(id: string) { return this.http.post<void>(`/api/KaggleAccounts/${id}/connect`, {}); }
  deleteKaggle(id: string) { return this.http.delete<void>(`/api/KaggleAccounts/${id}`); }

  users(page = 0, size = 50) { return this.http.get<{ users: AppUser[]; totalUsersCount: number }>('/api/Users/GetAllUsers', { params: { page, size } }); }
  createUser(b: object) { return this.http.post<any>('/api/Users/CreateUser', b); }
  assignRoles(userId: string, roles: string[]) { return this.http.post<any>('/api/Users/AssignRoleToUser', { userId, roles }); }
  deleteUser(id: string) { return this.http.delete<any>(`/api/Users/DeleteUser/${id}`); }
  roles(page = 0, size = 100) { return this.http.get<{ datas: Role[]; totalCount: number }>('/api/Roles/GetRoles', { params: { page, size } }); }
  createRole(name: string) { return this.http.post<any>('/api/Roles/CreateRole', { name }); }
  deleteRole(id: string) { return this.http.delete<any>(`/api/Roles/DeleteRole/${id}`); }
}
