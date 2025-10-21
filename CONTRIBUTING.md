# Contributing to Odin

Thank you for your interest in contributing to the Hugo Durable Orchestrator (Odin)!

## Code of Conduct

This project adheres to a code of conduct. By participating, you are expected to uphold this code.

## How to Contribute

### Reporting Bugs

Before creating bug reports, please check the issue list as you might find out that you don't need to create one. When you are creating a bug report, please include as many details as possible:

- **Use a clear and descriptive title**
- **Describe the exact steps to reproduce the problem**
- **Provide specific examples**
- **Describe the behavior you observed and what you expected**
- **Include logs, stack traces, or screenshots**
- **Note your environment** (.NET version, OS, database type)

### Suggesting Enhancements

Enhancement suggestions are tracked as GitHub issues. When creating an enhancement suggestion, please include:

- **Use a clear and descriptive title**
- **Provide a step-by-step description of the suggested enhancement**
- **Explain why this enhancement would be useful**
- **List any alternative solutions you've considered**

### Pull Requests

1. Fork the repo and create your branch from `main`
2. If you've added code that should be tested, add tests
3. Ensure the test suite passes
4. Make sure your code follows the existing style
5. Write a clear commit message
6. Update documentation as needed

## Development Process

### Setting Up Your Development Environment

```bash
# Clone your fork
git clone https://github.com/YOUR-USERNAME/Odin.git
cd Odin

# Add upstream remote
git remote add upstream https://github.com/df49b9cd/Odin.git

# Install dependencies
dotnet restore

# Build the solution
dotnet build

# Run tests
dotnet test
```

### Coding Standards

Follow the guidelines in [.github/copilot-instructions.md](.github/copilot-instructions.md):

1. **Hugo Integration**
   - Always use `using static Hugo.Go;`
   - Use WaitGroup, ErrGroup, Channels appropriately
   - Implement Result<T> pipelines for error handling

2. **Workflow Determinism**
   - Never use DateTime.Now, Random, or external I/O directly in workflows
   - Use DeterministicEffectStore for side effects
   - Use VersionGate for incompatible changes

3. **Testing**
   - Write unit tests for all new functionality
   - Include integration tests for workflow execution
   - Test deterministic replay
   - Validate Result<T> error handling

4. **Documentation**
   - Update API documentation
   - Add code examples
   - Update troubleshooting guides
   - Document migration impacts

### Testing Guidelines

```bash
# Run all tests
dotnet test

# Run specific test project
dotnet test tests/Odin.Core.Tests

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run integration tests
dotnet test tests/Odin.Integration.Tests
```

### Commit Message Format

Use conventional commits:

```
type(scope): subject

body

footer
```

Types:
- `feat`: New feature
- `fix`: Bug fix
- `docs`: Documentation only
- `style`: Code style changes
- `refactor`: Code refactoring
- `test`: Adding tests
- `chore`: Maintenance tasks

Examples:
```
feat(sdk): add support for async activities

Implement AsyncActivity base class with heartbeat support
and automatic retry policies.

Closes #123
```

### Branch Naming

- `feature/description` - New features
- `fix/description` - Bug fixes
- `docs/description` - Documentation updates
- `refactor/description` - Code refactoring

## Review Process

1. All submissions require review
2. We use GitHub pull requests for this purpose
3. Reviewers will check:
   - Code quality and style
   - Test coverage
   - Documentation updates
   - Breaking changes
   - Performance implications

## Community

- Ask questions in GitHub Discussions (coming soon)
- Report bugs via GitHub Issues
- Submit PRs for fixes and features

## License

By contributing, you agree that your contributions will be licensed under the MIT License.
