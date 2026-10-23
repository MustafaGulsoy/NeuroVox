-- BaseAuth permission for DELETE /api/TherapistAnnotations/{id}. Idempotent; run once per database:
--   docker exec -i neurovox-postgres psql -U neurovox -d NeuroVoxDb < deploy/endpoint-delete-annotations.sql
-- Every role that may create annotations (POST) may also take its own back.
INSERT INTO auth."Endpoints" ("Id","ActionType","HttpType","Definition","Code","MenuId","RowCreatedDate","RowIsActive","RowIsDeleted")
SELECT gen_random_uuid(),'Deleting','DELETE','Delete TherapistAnnotations','DELETE.Deleting.DeleteTherapistAnnotations',"MenuId",now(),true,false
FROM auth."Endpoints" WHERE "Code"='POST.Writing.PostTherapistAnnotations'
  AND NOT EXISTS (SELECT 1 FROM auth."Endpoints" WHERE "Code"='DELETE.Deleting.DeleteTherapistAnnotations');

INSERT INTO auth."AppRoleEndpoint" ("EndpointsId","RolesId")
SELECT d."Id", x."RolesId"
FROM auth."Endpoints" d
JOIN auth."Endpoints" p ON p."Code"='POST.Writing.PostTherapistAnnotations'
JOIN auth."AppRoleEndpoint" x ON x."EndpointsId"=p."Id"
WHERE d."Code"='DELETE.Deleting.DeleteTherapistAnnotations'
ON CONFLICT DO NOTHING;

-- PUT /api/SpeechRecordings/{id}/transcript: same roles as annotating.
INSERT INTO auth."Endpoints" ("Id","ActionType","HttpType","Definition","Code","MenuId","RowCreatedDate","RowIsActive","RowIsDeleted")
SELECT gen_random_uuid(),'Updating','PUT','Put SpeechRecordingTranscript','PUT.Updating.PutSpeechRecordingTranscript',"MenuId",now(),true,false
FROM auth."Endpoints" WHERE "Code"='POST.Writing.PostTherapistAnnotations'
  AND NOT EXISTS (SELECT 1 FROM auth."Endpoints" WHERE "Code"='PUT.Updating.PutSpeechRecordingTranscript');

INSERT INTO auth."AppRoleEndpoint" ("EndpointsId","RolesId")
SELECT d."Id", x."RolesId"
FROM auth."Endpoints" d
JOIN auth."Endpoints" p ON p."Code"='POST.Writing.PostTherapistAnnotations'
JOIN auth."AppRoleEndpoint" x ON x."EndpointsId"=p."Id"
WHERE d."Code"='PUT.Updating.PutSpeechRecordingTranscript'
ON CONFLICT DO NOTHING;
