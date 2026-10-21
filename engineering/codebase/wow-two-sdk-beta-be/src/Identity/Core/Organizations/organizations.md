# Identity.Core.Organizations

Organizations as an identity slice: one person, many organizations, one role per membership. Stored in
`identity_organizations` and `identity_memberships`; principals carry each membership's role and permissions.

## Quick start

```csharp
identity.AddOrganizations(o =>
{
    o.Roles["editor"] = ["can-edit"];                     // the owner role ("owner") always exists
    o.Roles["viewer"] = [];
    o.Roles["owner"] = ["can-edit", "can-approve"];       // optional: permissions for the owner too
});                                                        // after AddEntityFrameworkStores

var ocharo = new IdentityOrganization { Name = "Ocharo", Slug = "ocharo" };
await organizations.CreateAsync(ocharo, founder);          // the creator becomes owner
await organizations.AddMemberAsync(ocharo, member, "editor");
await organizations.ChangeRoleAsync(ocharo, member, "viewer");

principal.GetOrganizationRole(ocharo.Id);                  // "viewer"
principal.HasOrganizationPermission(ocharo.Id, "can-edit");
```

```jsonc
"Identity": { "Organizations": { "OwnerRole": "owner", "Roles": { "editor": [ "can-edit" ], "viewer": [] } } }
```

## Notes

- Role names compare case-insensitively and are stored as configured; an unconfigured role fails `UnknownOrganizationRole`.
- An organization keeps at least one owner: removing or demoting the last one fails `LastOrganizationOwner`.
- Removing a member, changing a role or deleting the organization rotates the member's stamp; joining appears at next sign-in.
- Claims are `org_role` = `{organizationId}:{role}` and `org_permission` = `{organizationId}:{permission}`; the
  types follow `IdentityClaimOptions` and the principal extensions take the type when a host changes it.
- Handles are unique, 1–64 lowercase letters, digits and single hyphens; names are 1–120 characters.
- Personal-data export adds an `organizations` section; account deletion removes the user's memberships, and an
  organization whose only owner is deleted keeps its other members without an owner.
- Existing databases need a migration for `identity_organizations` and `identity_memberships`.
