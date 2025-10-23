interface ErrorStateProps {
  title?: string;
  message?: string;
  action?: React.ReactNode;
}

function ErrorState({
  title = "Something went wrong",
  message = "We couldn't load this data. Please try again.",
  action
}: ErrorStateProps) {
  return (
    <div className="error-state" role="alert">
      <strong>{title}</strong>
      <div>{message}</div>
      {action ? <div style={{ marginTop: "0.75rem" }}>{action}</div> : null}
    </div>
  );
}

export default ErrorState;
