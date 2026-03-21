using HERMMapperApp.Models;

namespace HERMMapperApp.Infrastructure;

public static class ReferenceModelQueryExtensions
{
    public static IQueryable<TrmDomain> ForReferenceModel(this IQueryable<TrmDomain> query, ReferenceModelKind modelKind)
    {
        var prefix = ReferenceModelCatalog.GetDomainPrefix(modelKind);
        return query.Where(x => x.Code.StartsWith(prefix));
    }

    public static IQueryable<TrmCapability> ForReferenceModel(this IQueryable<TrmCapability> query, ReferenceModelKind modelKind)
    {
        var prefix = ReferenceModelCatalog.GetCapabilityPrefix(modelKind);
        return query.Where(x => x.Code.StartsWith(prefix));
    }

    public static IQueryable<TrmComponent> ForReferenceModel(this IQueryable<TrmComponent> query, ReferenceModelKind modelKind)
    {
        var prefix = ReferenceModelCatalog.GetComponentPrefix(modelKind);
        return modelKind == ReferenceModelKind.Trm
            ? query.Where(x => x.IsCustom || x.Code.StartsWith(prefix))
            : query.Where(x => !x.IsCustom && x.Code.StartsWith(prefix));
    }
}
