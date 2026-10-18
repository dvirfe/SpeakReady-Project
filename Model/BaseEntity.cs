namespace Model;

// בסיס משותף לישויות בעלות מזהה יחיד. GroupMember הוא חריג עם מפתח מורכב.
public abstract class BaseEntity
{
    public int Id { get; set; }
    protected BaseEntity() { }
    protected BaseEntity(int id) => Id = id;
}

