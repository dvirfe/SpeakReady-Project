namespace DAL;
public class DuplicateUserException : Exception
{
    public DuplicateUserException(Exception inner) : base("Username or email already exists.", inner) { }
}

    