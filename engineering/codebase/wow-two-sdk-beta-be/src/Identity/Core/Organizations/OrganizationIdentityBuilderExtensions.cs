using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Core.PersonalData;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;

/// <summary>Opts the organizations slice into an identity registration.</summary>
public static class OrganizationIdentityBuilderExtensions
{
    /// <summary>
    /// Registers <see cref="OrganizationService{TUser,TKey}"/> and, after <c>AddEntityFrameworkStores</c>, the
    /// repository over <c>identity_organizations</c> and <c>identity_memberships</c>. Roles and their permissions come
    /// from <paramref name="configure"/>, then the host section <c>Identity:Organizations</c>. Principals then carry
    /// organization role and permission claims.
    /// </summary>
    /// <typeparam name="TUser">The user entity.</typeparam>
    /// <typeparam name="TKey">The primary-key type.</typeparam>
    /// <param name="builder">The identity builder.</param>
    /// <param name="configure">The owner role and the other roles with their permissions.</param>
    public static IdentityBuilder<TUser, TKey> AddOrganizations<TUser, TKey>(this IdentityBuilder<TUser, TKey> builder, Action<OrganizationOptions>? configure = null)
        where TUser : IdentityUser<TKey>
        where TKey : notnull, IEquatable<TKey>
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddModuleOptions(
            OrganizationOptions.SectionName,
            configure,
            options => options
                .Validate(o => !string.IsNullOrWhiteSpace(o.OwnerRole), "OrganizationOptions.OwnerRole is required.")
                .Validate(o => o.Roles.Keys.All(name => !string.IsNullOrWhiteSpace(name)), "OrganizationOptions.Roles needs non-empty role names."));
        if (builder.ContextFactory is { } context)
            builder.Services.TryAddScoped<IOrganizationRepository<TKey>>(serviceProvider => new EfOrganizationRepository<TKey>(context(serviceProvider)));
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddScoped<OrganizationService<TUser, TKey>>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IPersonalDataExporter<TUser>, OrganizationPersonalDataExporter<TUser, TKey>>());
        return builder;
    }
}
