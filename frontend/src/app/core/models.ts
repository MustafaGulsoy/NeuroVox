export type AnalysisStatus = 0 | 1 | 2 | 3 | 4; // None, Queued, Running, Completed, Failed

export interface Participant {
  id: string; participantCode: string; dateOfBirth?: string | null; sex?: string | null; notes?: string | null;
  consentGivenAt?: string | null; consentVersion?: string | null; consentWithdrawnAt?: string | null; rowCreatedDate: string;
  diagnosis?: number | null; willingFollowUp6Months?: boolean | null; adequateVisionHearing?: boolean | null; severeMentalIllness?: boolean | null;
  severeNeurologicalDeficit?: boolean | null; languageBarrier?: boolean | null; eligibilityAssessedAt?: string | null; isEligible?: boolean | null;
}
export interface Visit { id: string; participantId: string; protocolId: string; visitType: number; scheduledDate?: string | null; actualDate?: string | null; notes?: string | null; }
export interface Recording {
  id: string; visitId: string; stimulusId: string; stimulusVersion: string; instructionVersion: string;
  recordingDurationSeconds: number; speechDurationSeconds?: number | null; audioQuality: number; hasAudio: boolean;
  transcriptText?: string | null; analysisStatus: AnalysisStatus; analysisError?: string | null; analyzedAt?: string | null;
}
export interface Measurement { featureName: string; numericValue: number | null; textValue: string | null; isCandidateAnnotation: boolean; definitionVersion?: string; modelVersion?: string; }
export interface AnalysisInfo { id: string; status: AnalysisStatus; analysisError?: string; analyzedAt?: string; measurements: Measurement[]; }
export interface Annotation { id: string; recordingId: string; raterIndex: number; category: number; severity?: string; confidence?: number; startSeconds: number; endSeconds: number; segmentText?: string; note?: string; submittedAt?: string; }
export interface Protocol { id: string; name: string; version: string; description?: string; targetRecordingSecondsMin: number; targetRecordingSecondsMax: number; codingManualVersion: string; isBlindedAnnotationEnabled: boolean; ethicsCommittee?: string; ethicsApprovalNumber?: string; ethicsApprovalDate?: string; }
export interface Stimulus { id: string; stimulusId: string; version: string; description?: string; protocolId: string; }
export interface FeatureDefinition { id: string; featureName: string; definition: string; algorithmVersion: string; validationStatus: number; unit?: string; layer: number; }
export interface Ace { id: string; participantId: string; visitId: string; assessmentVersion: string; assessmentDate: string; evaluator?: string; totalScore?: number; attentionScore?: number; memoryScore?: number; fluencyScore?: number; languageScore?: number; visuospatialScore?: number; }
export interface Outcome { id: string; participantId: string; outcomeType: number; diagnosisDate?: string; evaluator?: string; notes?: string; }
export interface Summary {
  participants: number; withConsent: number; visits: number; recordings: number; annotations: number; measurements: number;
  recordingsByStatus: { status: AnalysisStatus; count: number }[];
  recent: { id: string; visitId: string; analysisStatus: AnalysisStatus; rowCreatedDate: string; recordingDurationSeconds: number }[];
}
export interface AppUser { id: string; email?: string; nameSurname?: string; userName?: string; userRole?: string; }
export interface Role { id: string; name: string; }

export const VISIT_TYPES = ['Başlangıç', '6. ay', '12. ay', '18. ay', '24. ay', 'Diğer'];
export const OUTCOME_TYPES = ['Stabil MCI', "AD'ye dönüşüm", 'Diğer tanı', 'İyileşme', 'Takip kaybı'];
export const AUDIO_QUALITY = ['Bilinmiyor', 'Zayıf', 'Orta', 'İyi', 'Çok iyi'];
export const ANNOTATION_CATEGORIES = [
  'Anomi', 'Dolaylı anlatım', 'Belirsiz ifade', 'Boş ifade', 'Tekrar', 'Zamir belirsizliği', 'Anlamsal hata',
  'Morfosentaks sorunu', 'Bilgi atlama', 'Olay atlama', 'Tutarlılık sorunu', 'Bağdaşıklık sorunu', 'Konudan sapma'
];
export const STATUS_LABEL: Record<number, string> = { 0: 'Beklemede', 1: 'Kuyrukta', 2: 'Çalışıyor', 3: 'Tamamlandı', 4: 'Başarısız' };
export const VALIDATION_LABEL = ['Deneysel', 'Onay bekliyor', 'Protokol onaylı'];

export interface KaggleAccount {
  id: string; username: string; status: 'online' | 'starting' | 'offline' | 'error';
  mode?: 'gpu' | 'cpu' | null; gpuHoursUsed: number; gpuHoursLimit: number; gpuExhausted: boolean;
  lastHeartbeatUtc?: string; kernelStartedUtc?: string; lastError?: string;
}
export interface KaggleOverview { secretsEnabled: boolean; alwaysOn: boolean; queue: { recordings: number; training: number }; accounts: KaggleAccount[]; }
export interface ModelMetrics { accuracy: number; f1: number; roc_auc: number; roc_auc_ci95?: number[] | null; sensitivity?: number | null; specificity?: number | null; }
export interface TrainingRun {
  id: string; status: AnalysisStatus; error?: string; sampleCount: number; rowCreatedDate: string; startedAt?: string; finishedAt?: string;
  report?: { n_rows: number; n_participants: number; folds: number; primary_set: string; models: Record<string, ModelMetrics>;
    feature_sets: Record<string, { features: string[]; models: Record<string, ModelMetrics> }>; note: string };
}
export interface GroupResult {
  feature: string; status: string; test?: string; statistic?: number; p?: number; p_adj?: number; effect_name?: string; effect?: number;
  a: { n: number; median?: number; q1?: number; q3?: number }; b: { n: number; median?: number; q1?: number; q3?: number }; shapiro_p?: { a: number | null; b: number | null };
}
export interface GroupComparison { by: string; groups: { a: { name: string; n: number }; b: { name: string; n: number } }; results: GroupResult[]; note?: string; }
export interface RaterAgreement {
  annotatedRecordings: number; recordingsWithTwoRaters: number; note: string;
  categories: { category: string; n: number; percentAgreement: number; kappa: number | null }[];
  aiVsHuman: { category: string; feature: string; n: number; spearman: number | null }[];
}
export interface VisitPrediction { visitId: string; modelEstimatedRisk: number | null; predictedLabel: number | null; featuresUsed: number; featuresExpected: number; missing: string[]; disclaimer: string; }
export const DIAGNOSES = ['MCI (hafif bilişsel bozukluk)', 'Hafif Alzheimer'];
