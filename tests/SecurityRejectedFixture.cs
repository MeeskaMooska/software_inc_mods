using System;
public static class SecurityRejectedFixture
{
    public static string ErrorType(Exception error) { return error.GetType().Name; }
}
