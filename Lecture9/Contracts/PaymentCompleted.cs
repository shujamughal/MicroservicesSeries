namespace Contracts;

public record PaymentCompleted(int OrderId, decimal Amount);

public record BookPriceUpdated(int BookId, decimal NewPrice);
