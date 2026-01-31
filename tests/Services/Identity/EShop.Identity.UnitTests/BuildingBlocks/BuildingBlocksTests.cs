using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Exceptions;
using EShop.BuildingBlocks.Domain;
using FluentValidation.Results;

namespace EShop.Identity.UnitTests.BuildingBlocks;

[TestFixture]
public class ResultTests
{
    [Test]
    public void Success_ShouldCreateSuccessResult()
    {
        // Act
        var result = Result<string>.Success("test value");

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.IsFailure, Is.False);
        Assert.That(result.Value, Is.EqualTo("test value"));
        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public void Failure_ShouldCreateFailureResult()
    {
        // Arrange
        var error = new Error("Test.Error", "Test error message");

        // Act
        var result = Result<string>.Failure(error);

        // Assert
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Value, Is.Null);
        Assert.That(result.Error, Is.EqualTo(error));
    }

    [Test]
    public void Match_OnSuccess_ShouldCallOnSuccessFunction()
    {
        // Arrange
        var result = Result<int>.Success(42);

        // Act
        var matched = result.Match(
            onSuccess: value => $"Value is {value}",
            onFailure: error => $"Error: {error.Message}");

        // Assert
        Assert.That(matched, Is.EqualTo("Value is 42"));
    }

    [Test]
    public void Match_OnFailure_ShouldCallOnFailureFunction()
    {
        // Arrange
        var error = new Error("Test.Error", "Something went wrong");
        var result = Result<int>.Failure(error);

        // Act
        var matched = result.Match(
            onSuccess: value => $"Value is {value}",
            onFailure: err => $"Error: {err.Message}");

        // Assert
        Assert.That(matched, Is.EqualTo("Error: Something went wrong"));
    }

    [Test]
    public void ImplicitConversion_FromValue_ShouldCreateSuccessResult()
    {
        // Act
        Result<string> result = "implicit value";

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.EqualTo("implicit value"));
    }

    [Test]
    public void ImplicitConversion_FromError_ShouldCreateFailureResult()
    {
        // Arrange
        var error = new Error("Test.Error", "Test message");

        // Act
        Result<string> result = error;

        // Assert
        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(error));
    }

    [Test]
    public void Switch_OnSuccess_ShouldCallSuccessAction()
    {
        // Arrange
        var result = Result<int>.Success(42);
        var successCalled = false;
        var failureCalled = false;

        // Act
        result.Switch(
            onSuccess: _ => successCalled = true,
            onFailure: _ => failureCalled = true);

        // Assert
        Assert.That(successCalled, Is.True);
        Assert.That(failureCalled, Is.False);
    }

    [Test]
    public void Switch_OnFailure_ShouldCallFailureAction()
    {
        // Arrange
        var result = Result<int>.Failure(new Error("Test", "Error"));
        var successCalled = false;
        var failureCalled = false;

        // Act
        result.Switch(
            onSuccess: _ => successCalled = true,
            onFailure: _ => failureCalled = true);

        // Assert
        Assert.That(successCalled, Is.False);
        Assert.That(failureCalled, Is.True);
    }
}

[TestFixture]
public class ValidationExceptionTests
{
    [Test]
    public void Constructor_WithFluentValidationFailures_ShouldGroupByProperty()
    {
        // Arrange
        var failures = new List<ValidationFailure>
        {
            new("Email", "Email is required"),
            new("Email", "Email format is invalid"),
            new("Password", "Password is too short")
        };

        // Act
        var exception = new ValidationException(failures);

        // Assert
        Assert.That(exception.Errors, Has.Count.EqualTo(2));
        Assert.That(exception.Errors["Email"], Has.Length.EqualTo(2));
        Assert.That(exception.Errors["Password"], Has.Length.EqualTo(1));
    }

    [Test]
    public void Constructor_Default_ShouldHaveEmptyErrors()
    {
        // Act
        var exception = new ValidationException();

        // Assert
        Assert.That(exception.Errors, Is.Empty);
        Assert.That(exception.Message, Is.EqualTo("One or more validation failures have occurred."));
    }
}

[TestFixture]
public class AggregateRootTests
{
    [Test]
    public void AddDomainEvent_ShouldAddEventToList()
    {
        // Arrange
        var aggregate = new TestAggregate();
        var domainEvent = new TestDomainEvent();

        // Act
        aggregate.RaiseEvent(domainEvent);

        // Assert
        Assert.That(aggregate.DomainEvents, Has.Count.EqualTo(1));
        Assert.That(aggregate.DomainEvents[0], Is.EqualTo(domainEvent));
    }

    [Test]
    public void ClearDomainEvents_ShouldRemoveAllEvents()
    {
        // Arrange
        var aggregate = new TestAggregate();
        aggregate.RaiseEvent(new TestDomainEvent());
        aggregate.RaiseEvent(new TestDomainEvent());

        // Act
        aggregate.ClearDomainEvents();

        // Assert
        Assert.That(aggregate.DomainEvents, Is.Empty);
    }
}

// Test classes
public class TestAggregate : AggregateRoot<Guid>
{
    public TestAggregate()
    {
        Id = Guid.NewGuid();
    }

    public void RaiseEvent(IDomainEvent domainEvent)
    {
        AddDomainEvent(domainEvent);
    }
}

public class TestDomainEvent : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
