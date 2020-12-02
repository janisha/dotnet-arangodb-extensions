using System; 
using System.Linq.Expressions;

namespace Core.Arango.Linq.Internal.Util.ExtendedMethods
{
    public class LimitExpression : Expression
    {
        public override ExpressionType NodeType => ExpressionType.Extension;

        public override Type Type { get { return typeof(int); } }

        public override bool CanReduce => true;

        public override Expression Reduce()
        {
            return Expression.Constant(42);
        }

        protected override Expression Accept(ExpressionVisitor visitor)
        {
            return visitor is ICustomExpressionVisitor customValidator ? customValidator.VisitAnswerToEverythingExpression(this) : base.Accept(visitor);
        }
    }

    public interface ICustomExpressionVisitor
    {
        Expression VisitAnswerToEverythingExpression(LimitExpression node);
    }


    public abstract class CustomExpressionVisitor : ExpressionVisitor, ICustomExpressionVisitor
    {
        public virtual Expression VisitAnswerToEverythingExpression(LimitExpression node)
        {
            return node;
        }
    }
}
