using JobOfferMatcher.Domain.Common.Ids;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace JobOfferMatcher.Infrastructure.Persistence.Converters;

// EF value converters mapping each wrapped id <-> Guid, registered globally in
// AppDbContext.ConfigureConventions so no raw Guid leaks into the model (Principle II).

public sealed class OfferIdConverter() : ValueConverter<OfferId, Guid>(id => id.Value, v => OfferId.From(v));

public sealed class SourceIdConverter() : ValueConverter<SourceId, Guid>(id => id.Value, v => SourceId.From(v));

public sealed class ScanRunIdConverter() : ValueConverter<ScanRunId, Guid>(id => id.Value, v => ScanRunId.From(v));

public sealed class OfferVersionIdConverter() : ValueConverter<OfferVersionId, Guid>(id => id.Value, v => OfferVersionId.From(v));

public sealed class OfferObservationIdConverter() : ValueConverter<OfferObservationId, Guid>(id => id.Value, v => OfferObservationId.From(v));

public sealed class OfferEventIdConverter() : ValueConverter<OfferEventId, Guid>(id => id.Value, v => OfferEventId.From(v));

public sealed class RoleGroupIdConverter() : ValueConverter<RoleGroupId, Guid>(id => id.Value, v => RoleGroupId.From(v));

public sealed class CvIdConverter() : ValueConverter<CvId, Guid>(id => id.Value, v => CvId.From(v));

// Application tracking (005).
public sealed class PipelineStageIdConverter() : ValueConverter<PipelineStageId, Guid>(id => id.Value, v => PipelineStageId.From(v));

public sealed class ApplicationNoteIdConverter() : ValueConverter<ApplicationNoteId, Guid>(id => id.Value, v => ApplicationNoteId.From(v));

public sealed class ApplicationTaskIdConverter() : ValueConverter<ApplicationTaskId, Guid>(id => id.Value, v => ApplicationTaskId.From(v));

public sealed class ApplicationDocumentIdConverter() : ValueConverter<ApplicationDocumentId, Guid>(id => id.Value, v => ApplicationDocumentId.From(v));

public sealed class ApplicationCommunicationIdConverter() : ValueConverter<ApplicationCommunicationId, Guid>(id => id.Value, v => ApplicationCommunicationId.From(v));

public sealed class ApplicationInterviewIdConverter() : ValueConverter<ApplicationInterviewId, Guid>(id => id.Value, v => ApplicationInterviewId.From(v));
