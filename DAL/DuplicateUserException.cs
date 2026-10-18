namespace DAL;

// חריגה משלנו כדי שה-API לא יצטרך הפניה ישירה לספריית MySQL.
public class DuplicateUserException : Exception
{
    public DuplicateUserException(Exception inner) : base("Username or email already exists.", inner) { }
}

    