# Centralized NuGet Package Management

Odin uses [Central Package Management (CPM)](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management) to manage NuGet package versions across all projects in the solution.

## Overview

With CPM, all package versions are defined centrally in `Directory.Packages.props`, and individual project files reference packages without specifying versions.

### Benefits

- **Single source of truth** - All package versions defined in one place
- **Consistency** - Ensures all projects use the same version of shared dependencies
- **Easier updates** - Update a package version in one location
- **Reduced conflicts** - Eliminates version mismatches across projects
- **Cleaner project files** - Project files only list package names, not versions

## File Structure

### Directory.Packages.props

Located at the solution root, this file contains all package version definitions:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>

  <ItemGroup>
    <PackageVersion Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.0-rc.2.25502.107" />
    <!-- More packages... -->
  </ItemGroup>
</Project>
```

### Directory.Build.props

Defines common packages that all projects should reference:

```xml
<Project>
  <!-- Common packages for all projects -->
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
  </ItemGroup>

  <!-- Test project packages -->
  <ItemGroup Condition="'$(IsTestProject)' == 'true'">
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <!-- More test packages... -->
  </ItemGroup>
</Project>
```

### Individual Project Files

Project files reference packages **without** version attributes:

```xml
<!-- ✅ Correct - No version -->
<PackageReference Include="Microsoft.AspNetCore.OpenApi" />

<!-- ❌ Wrong - Version specified (will cause error) -->
<PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="10.0.0" />
```

## Package Categories

Packages are organized by category in `Directory.Packages.props`:

### Common Packages
- Microsoft.Extensions.Logging.Abstractions
- Microsoft.Extensions.DependencyInjection.Abstractions
- Microsoft.Extensions.Configuration.Abstractions
- Microsoft.Extensions.Hosting

### ASP.NET Core
- Microsoft.AspNetCore.OpenApi
- Swashbuckle.AspNetCore

### gRPC
- Grpc.AspNetCore
- Grpc.AspNetCore.Server.Reflection
- Grpc.Tools
- Google.Protobuf

### Database
- Npgsql (PostgreSQL)
- MySql.Data (MySQL)
- Dapper (micro-ORM)

### Elasticsearch (Optional)
- Elastic.Clients.Elasticsearch

### OpenTelemetry
- OpenTelemetry
- OpenTelemetry.Exporter.OpenTelemetryProtocol
- OpenTelemetry.Exporter.Prometheus.AspNetCore
- OpenTelemetry.Extensions.Hosting
- OpenTelemetry.Instrumentation.*

### Testing
- xunit
- xunit.runner.visualstudio
- coverlet.collector
- FluentAssertions
- Moq
- Microsoft.NET.Test.Sdk

## Adding a New Package

### 1. Add Version to Directory.Packages.props

```xml
<ItemGroup>
  <PackageVersion Include="Newtonsoft.Json" Version="13.0.3" />
</ItemGroup>
```

### 2. Reference in Project File (without version)

```xml
<ItemGroup>
  <PackageReference Include="Newtonsoft.Json" />
</ItemGroup>
```

### 3. Restore Packages

```bash
dotnet restore
```

## Updating Package Versions

To update a package version across all projects:

1. Edit `Directory.Packages.props`
2. Update the version attribute for the package
3. Run `dotnet restore`
4. Test and verify the update

Example:
```xml
<!-- Before -->
<PackageVersion Include="Npgsql" Version="9.0.0" />

<!-- After -->
<PackageVersion Include="Npgsql" Version="9.0.1" />
```

## Test Projects Configuration

Test projects are automatically configured with common test packages through `Directory.Build.props`.

To mark a project as a test project, add this property:

```xml
<PropertyGroup>
  <IsTestProject>true</IsTestProject>
</PropertyGroup>
```

This automatically includes:
- xunit
- xunit.runner.visualstudio
- coverlet.collector
- FluentAssertions
- Moq
- Microsoft.NET.Test.Sdk

## Common Commands

```bash
# Restore all packages
dotnet restore

# Build with restored packages
dotnet build

# Check for outdated packages
dotnet list package --outdated

# Check for vulnerable packages
dotnet list package --vulnerable

# Update a specific package (update in Directory.Packages.props first)
dotnet restore

# Clean and restore
dotnet clean && dotnet restore && dotnet build
```

## Troubleshooting

### Error: "PackageReference items cannot define a value for Version"

**Cause**: A project file has a `Version` attribute on a `PackageReference`.

**Solution**: Remove the `Version` attribute from the project file. Versions should only be in `Directory.Packages.props`.

```xml
<!-- Wrong -->
<PackageReference Include="SomePackage" Version="1.0.0" />

<!-- Correct -->
<PackageReference Include="SomePackage" />
```

### Error: "Package not found"

**Cause**: Package is referenced in a project but not defined in `Directory.Packages.props`.

**Solution**: Add the package version to `Directory.Packages.props`:

```xml
<PackageVersion Include="MissingPackage" Version="x.y.z" />
```

### Warning: "Detected package downgrade"

**Cause**: A dependency requires a newer version than specified in `Directory.Packages.props`.

**Solution**: Update the version in `Directory.Packages.props` to match or exceed the required version.

### Warning: "PackageReference will not be pruned"

**Cause**: A package is explicitly referenced but is already included transitively by another package.

**Solution**: This is informational. You can either:
1. Remove the explicit reference (rely on transitive dependency)
2. Keep it for explicitness (recommended for critical dependencies)

## Best Practices

1. **Version Consistency**: Always use CPM for all projects in the solution
2. **Semantic Versioning**: Follow semantic versioning when updating packages
3. **Test Updates**: Always test after updating package versions
4. **Document Breaking Changes**: Note any breaking changes in CHANGELOG.md
5. **Transitive Dependencies**: Be aware of transitive dependency versions
6. **Security Updates**: Regularly check for security vulnerabilities
7. **Preview Packages**: Mark preview/RC packages clearly in comments

## References

- [Microsoft Docs: Central Package Management](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management)
- [NuGet CPM GitHub Issue](https://github.com/NuGet/Home/wiki/Centrally-managing-NuGet-package-versions)
- [Directory.Packages.props Spec](https://github.com/NuGet/Home/blob/dev/designs/CentralPackageManagement.md)

## Current Package Versions

See [Directory.Packages.props](../Directory.Packages.props) for the complete list of managed packages and their versions.

Key versions:
- .NET: 10.0.0-rc.2
- ASP.NET Core: 10.0.0-rc.2
- gRPC: 2.70.0
- OpenTelemetry: 1.10.0
- xUnit: 2.9.3
