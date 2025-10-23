interface LoadingStateProps {
  message?: string;
}

function LoadingState({ message = "Loading data..." }: LoadingStateProps) {
  return <div className="loading-state" role="status">{message}</div>;
}

export default LoadingState;
